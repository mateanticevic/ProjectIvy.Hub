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

    private readonly ConcurrentDictionary<string, int?> _cityCache = new ConcurrentDictionary<string, int?>();

    private readonly ConcurrentDictionary<string, int?> _countryCache = new ConcurrentDictionary<string, int?>();

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
            var resolved = await ResolveCity(tracking.Geohash);
            if (resolved.CountryId.HasValue || resolved.CityId.HasValue)
            {
                using var sqlConnection = GetSqlConnection();
                await sqlConnection.ExecuteAsync("UPDATE Tracking.Tracking SET CityId = @CityId, CountryId = @CountryId WHERE Id = @Id", new { resolved.CityId, tracking.Id, resolved.CountryId });
                _logger.LogInformation("Tracking {Id} resolved, queue count: {Count}", tracking.Id, _trackingQueue.Count);
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

    private async Task<(int? CityId, int? CountryId)> ResolveCity(string geohash)
    {
        var cityFromCache = _cityCache.Where(x => geohash.StartsWith(x.Key)).OrderByDescending(x => x.Key.Length).FirstOrDefault();

        bool cityResolvedFromCache = cityFromCache.Key != null;
        int? resolvedCityId = cityResolvedFromCache ? cityFromCache.Value : null;

        if (!cityResolvedFromCache)
            resolvedCityId = await SearchCity(geohash);

        var countryFromCache = _countryCache.Where(x => geohash.StartsWith(x.Key)).OrderByDescending(x => x.Key.Length).FirstOrDefault();

        bool countryResolvedFromCache = countryFromCache.Key != null;
        int? resolvedCountryId = countryResolvedFromCache ? countryFromCache.Value : null;

        if (!countryResolvedFromCache)
            resolvedCountryId = await SearchCountry(geohash);

        return (resolvedCityId, resolvedCountryId);
    }

    private async Task<int?> SearchCity(string geohash)
    {
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
            string largestEmptyParentGeohash = await FindLargestEmptyParentGeohash(geohash);
            _cityCache.TryAdd(largestEmptyParentGeohash, null);

            _logger.LogInformation("City not found, largest empty parent {Geohash}", largestEmptyParentGeohash);

            return null;
        }

        _cityCache.TryAdd(cityGeohash.Geohash, cityGeohash.CityId);

        _logger.LogInformation("City {CityId} found", cityGeohash.CityId);

        return cityGeohash.CityId;
    }

    private async Task<int?> SearchCountry(string geohash)
    {
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

        var countryGeohash = await sqlConnection.QueryFirstOrDefaultAsync<CountryGeohash>("SELECT TOP 1 CountryId, Geohash FROM Common.CountryGeohash WHERE Geohash IN @Geohashes", new { Geohashes = superGeohashes });

        if (countryGeohash == null)
        {
            string largestEmptyParentGeohash = await FindLargestEmptyParentGeohashForCountries(geohash);
            _countryCache.TryAdd(largestEmptyParentGeohash, null);

            _logger.LogInformation("Country not found, largest empty parent {Geohash}", largestEmptyParentGeohash);

            return null;
        }

        _countryCache.TryAdd(countryGeohash.Geohash, countryGeohash.CountryId);

        _logger.LogInformation("Country {CountryId} found", countryGeohash.CountryId);

        return countryGeohash.CountryId;
    }

    private async Task<string> FindLargestEmptyParentGeohash(string geohash)
    {
        using var sqlConnection = GetSqlConnection();

        for (int i = 2; i < geohash.Length; i++)
        {
            string searchGeohash = geohash.Substring(0, i);
            string match = await sqlConnection.QueryFirstOrDefaultAsync<string>("SELECT TOP 1 Geohash FROM Common.CityGeohash WHERE CHARINDEX(@Geohash, Geohash) = 1", new { Geohash = searchGeohash });

            if (match == null)
                return searchGeohash;
        }

        return geohash;
    }

    private async Task<string> FindLargestEmptyParentGeohashForCountries(string geohash)
    {
        using var sqlConnection = GetSqlConnection();

        for (int i = 2; i < geohash.Length; i++)
        {
            string searchGeohash = geohash.Substring(0, i);
            string match = await sqlConnection.QueryFirstOrDefaultAsync<string>("SELECT TOP 1 Geohash FROM Common.CountryGeohash WHERE CHARINDEX(@Geohash, Geohash) = 1", new { Geohash = searchGeohash });

            if (match == null)
                return searchGeohash;
        }

        return geohash;
    }

    private SqlConnection GetSqlConnection()
        => new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN"));
}
