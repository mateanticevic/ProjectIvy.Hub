using Dapper;
using Geohash;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Hubs;

public class TrackingHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly ILogger _logger;

    private readonly ConcurrentQueue<TrackingForProcessing> _trackingQueue = new ConcurrentQueue<TrackingForProcessing>();

    public TrackingHub(ILogger<TrackingHub> logger)
    {
        _logger = logger;
        Task.Run(ProcessTrackingQueueAsync);
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        string ipAddress = httpContext.Request.Headers["X-Forwarded-For"].ToString() ?? httpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
        _logger.LogInformation("Client connected {IpAddress}", ipAddress);

        using (var sqlConnection = new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN")))
        {
            await sqlConnection.OpenAsync();
            var tracking = await sqlConnection.QueryFirstOrDefaultAsync<Tracking>("SELECT TOP 1 * FROM Tracking.Tracking WHERE UserId = 1 ORDER BY Timestamp DESC");

            if (tracking != null)
                await Clients.Caller.SendAsync(TrackingEvents.Receive, tracking);
        }

        await base.OnConnectedAsync();
        return;
    }

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
        var geohasher = new Geohasher();
        using var sqlConnection = new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN"));
        var param = new
        {
            tracking.Accuracy,
            tracking.Altitude,
            Geohash = geohasher.Encode((double)tracking.Latitude, (double)tracking.Longitude, 9),
            tracking.Latitude,
            tracking.Longitude,
            tracking.Speed,
            tracking.Timestamp,
            tracking.UserId
        };
        long id = await sqlConnection.QuerySingleAsync<long>(@"INSERT INTO Tracking.Tracking (Accuracy, Altitude, Latitude, Longitude, Timestamp, Speed, UserId, Geohash)
                                                            OUTPUT INSERTED.Id
                                                            VALUES (@Accuracy, @Altitude, @Latitude, @Longitude, @Timestamp, @Speed, @UserId, @Geohash)", param);

        _trackingQueue.Enqueue(new TrackingForProcessing { Id = id, Geohash = param.Geohash, UserId = param.UserId });
    }

    private async Task ProcessTrackingQueueAsync()
    {
        while (true)
        {
            while (_trackingQueue.TryDequeue(out TrackingForProcessing item))
            {
                await ProcessTracking(item);
                _logger.LogInformation("Queue count: {Count}", _trackingQueue.Count);
            }

            await Task.Delay(1000);
        }

    }

    private async Task ProcessTracking(TrackingForProcessing tracking)
    {
    }
}
