using System;
using System.Threading.Tasks;
using Dapper;
using Geohash;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;
using ProjectIvy.Hub.Services;

namespace ProjectIvy.Hub.Hubs;

public class TrackingHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly ILogger _logger;
    private readonly TrackingProcessingService _processingService;
    private readonly IMemoryCache _memoryCache;

    public TrackingHub(ILogger<TrackingHub> logger, TrackingProcessingService processingService, IMemoryCache memoryCache)
    {
        _logger = logger;
        _processingService = processingService;
        _memoryCache = memoryCache;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        string ipAddress = httpContext.Request.Headers["X-Forwarded-For"].ToString() ?? httpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
        _logger.LogInformation("Client connected {IpAddress}", ipAddress);

        using (var sqlConnection = GetSqlConnection())
        {
            await sqlConnection.OpenAsync();
            var tracking = await sqlConnection.QueryFirstOrDefaultAsync<Tracking>("SELECT TOP 1 * FROM Tracking.Tracking WHERE UserId = 1 ORDER BY Timestamp DESC");

            if (tracking != null)
                await Clients.Caller.SendAsync(TrackingEvents.Receive, tracking);
        }

        await base.OnConnectedAsync();
        return;
    }

    [Authorize]
    public async Task Send(Tracking tracking)
    {
        _logger.LogInformation("Tracking broadcast");
        _ = Task.Run(async () => await SaveTracking(tracking));

        await Clients.All.SendAsync(TrackingEvents.Receive, tracking);
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        _logger.LogInformation("Client disconnected");

        if (exception is not null)
            _logger.LogError(exception, "Unexpected disconnect");

        return base.OnDisconnectedAsync(exception);
    }

    private async Task SaveTracking(Tracking tracking)
    {
        var username = Context.User?.FindFirst("preferred_username")?.Value;
        
        using var sqlConnection = GetSqlConnection();
        await sqlConnection.OpenAsync();
        
        var cacheKey = $"userId_{username}";
        var userId = await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await sqlConnection.QuerySingleOrDefaultAsync<int?>("SELECT Id FROM [User].[User] WHERE Username = @Username", new { Username = username });
        });
        
        var geohasher = new Geohasher();
        var param = new
        {
            tracking.Accuracy,
            tracking.Altitude,
            Geohash = geohasher.Encode((double)tracking.Latitude, (double)tracking.Longitude, 9),
            tracking.Latitude,
            tracking.Longitude,
            tracking.Speed,
            tracking.Timestamp,
            UserId = userId
        };
        long id = await sqlConnection.QuerySingleAsync<long>(@"INSERT INTO Tracking.Tracking (Accuracy, Altitude, Latitude, Longitude, Timestamp, Speed, UserId, Geohash)
                                                            OUTPUT INSERTED.Id
                                                            VALUES (@Accuracy, @Altitude, @Latitude, @Longitude, @Timestamp, @Speed, @UserId, @Geohash)", param);

        _processingService.EnqueueTracking(new TrackingForProcessing { Id = id, Geohash = param.Geohash, UserId = userId.GetValueOrDefault() });
    }

    private SqlConnection GetSqlConnection()
        => new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN"));
}
