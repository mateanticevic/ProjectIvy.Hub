using Dapper;
using Geohash;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Hubs;

public class TrackingHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly ILogger _logger;

    private readonly IMemoryCache _memoryCache;

    private readonly ConcurrentDictionary<string, City> _cityCache = new ConcurrentDictionary<string, City>();

    private readonly ConcurrentQueue<TrackingForProcessing> _trackingQueue = new ConcurrentQueue<TrackingForProcessing>();

    public TrackingHub(ILogger<TrackingHub> logger, IMemoryCache memoryCache)
    {
        _logger = logger;
        Task.Run(ProcessTrackingQueueAsync);
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
        using var sqlConnection = GetSqlConnection();
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
                _logger.LogInformation("Queue count: {Count}, Cache size: {CacheSize}", _trackingQueue.Count, _cityCache.Count);
            }

            await Task.Delay(1000);
        }
    }

    private async Task ProcessTracking(TrackingForProcessing tracking)
    {
        await ProcessTrackingForCity(tracking);
    }

    private async Task<bool> ProcessTrackingForCity(TrackingForProcessing tracking)
    {
        try
        {
            var city = await ResolveCity(tracking.Geohash);
            if (city?.Id.HasValue == true || city?.CountryId.HasValue == true)
            {
                using var sqlConnection = GetSqlConnection();
                await sqlConnection.ExecuteAsync("UPDATE Tracking.Tracking SET CityId = @CityId, CountryId = @CountryId WHERE Id = @Id", new { CityId = city.Id, tracking.Id, city.CountryId });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing tracking for city");
            return false;
        }
    }

    private async Task<City> ResolveCity(string geohash)
    {
        var cityFromCache = _cityCache.SingleOrDefault(x => geohash.StartsWith(x.Key));

        if (cityFromCache.Value != null)
            return cityFromCache.Value;

        var superGeohashes = new List<string>
        {
            geohash.Substring(0, 8),
            geohash.Substring(0, 7),
            geohash.Substring(0, 6),
            geohash.Substring(0, 5),
            geohash.Substring(0, 4),
            geohash.Substring(0, 3),
            geohash.Substring(0, 2),
        };

        using var sqlConnection = GetSqlConnection();

        var cityGeohash = await sqlConnection.QueryFirstOrDefaultAsync<CityGeohash>("SELECT TOP 1 CityId, Geohash FROM Common.CityGeohash WHERE Geohash IN @Geohashes", new { Geohashes = superGeohashes });

        if (cityGeohash == null)
        {
            var countryGeohash = await sqlConnection.QueryFirstOrDefaultAsync<CountryGeohash>("SELECT TOP 1 CountryId, Geohash FROM Common.CountryGeohash WHERE Geohash IN @Geohashes", new { Geohashes = superGeohashes });

            if (countryGeohash == null)
                return null;

            var cacheItem = new City { CountryId = countryGeohash.CountryId };

            _cityCache.TryAdd(countryGeohash.Geohash, cacheItem);

            _logger.LogInformation("Country resolved {CountryId}", cacheItem.CountryId);

            return cacheItem;
        }

        int countryId = await sqlConnection.ExecuteScalarAsync<int>("SELECT CountryId FROM Common.City WHERE Id = @CityId", new { cityGeohash.CityId });

        var city = new City { Id = cityGeohash.CityId, CountryId = countryId };

        _cityCache.TryAdd(cityGeohash.Geohash, city);

        _logger.LogInformation("City resolved {CityId}", city.Id);

        return city;
    }

    private SqlConnection GetSqlConnection()
        => new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN"));
}
