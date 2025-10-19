using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectIvy.Hub.Models;

namespace ProjectIvy.Hub.Services;

public class TrackingProcessingService : BackgroundService
{
    private readonly ILogger<TrackingProcessingService> _logger;
    
    private readonly ConcurrentDictionary<string, int?> _cityCache = new ConcurrentDictionary<string, int?>();
    private readonly ConcurrentDictionary<string, int?> _countryCache = new ConcurrentDictionary<string, int?>();
    private readonly ConcurrentDictionary<(int UserId, string geohash), int?> _locationCache = new ConcurrentDictionary<(int UserId, string geohash), int?>();
    private readonly ConcurrentQueue<TrackingForProcessing> _trackingQueue = new ConcurrentQueue<TrackingForProcessing>();

    public ConcurrentDictionary<string, int?> CityCache => _cityCache;
    public ConcurrentDictionary<string, int?> CountryCache => _countryCache;
    public ConcurrentDictionary<(int UserId, string geohash), int?> LocationCache => _locationCache;
    
    public TrackingProcessingService(ILogger<TrackingProcessingService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TrackingProcessingService starting - Loading caches...");
        
        // Load location geohashes cache
        using (var sqlConnection = GetSqlConnection())
        {
            var locationGeohashes = await sqlConnection.QueryAsync<(int UserId, int LocationId, string geohash)>(
                "SELECT l.UserId, lg.LocationId, lg.Geohash FROM Tracking.LocationGeohash lg JOIN Tracking.Location l ON lg.LocationId = l.Id");

            foreach (var locationGeohash in locationGeohashes)
            {
                _locationCache.TryAdd((locationGeohash.UserId, locationGeohash.geohash), locationGeohash.LocationId);
            }
            
            _logger.LogInformation("Loaded {Count} location geohashes", _locationCache.Count);
        }

        _logger.LogInformation("TrackingProcessingService initialized - Starting background processing");

        // Process queue
        while (!stoppingToken.IsCancellationRequested)
        {
            while (_trackingQueue.TryDequeue(out TrackingForProcessing item))
            {
                await ProcessTracking(item);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    public void EnqueueTracking(TrackingForProcessing tracking)
    {
        _trackingQueue.Enqueue(tracking);
    }

    private async Task ProcessTracking(TrackingForProcessing tracking)
    {
        try
        {
            int? cityId = await ResolveCity(tracking.Geohash);
            int? countryId = await ResolveCountry(tracking.Geohash);
            int? locationId = ResolveLocation(tracking.UserId, tracking.Geohash);
            
            if (countryId.HasValue || cityId.HasValue || locationId.HasValue)
            {
                using var sqlConnection = GetSqlConnection();
                await sqlConnection.ExecuteAsync(
                    "UPDATE Tracking.Tracking SET CityId = @CityId, CountryId = @CountryId, LocationId = @LocationId WHERE Id = @Id",
                    new { cityId, tracking.Id, countryId, LocationId = locationId });
                
                _logger.LogInformation("Tracking {Id} resolved, queue count: {Count}", tracking.Id, _trackingQueue.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing tracking");
        }
    }

    private async Task<int?> ResolveCity(string geohash)
    {
        var cityFromCache = _cityCache.Where(x => geohash.StartsWith(x.Key)).OrderByDescending(x => x.Key.Length).FirstOrDefault();

        bool cityResolvedFromCache = cityFromCache.Key != null;
        int? resolvedCityId = cityResolvedFromCache ? cityFromCache.Value : null;

        if (!cityResolvedFromCache)
            resolvedCityId = await SearchCity(geohash);

        return resolvedCityId;
    }

    private async Task<int?> ResolveCountry(string geohash)
    {
        var countryFromCache = _countryCache.Where(x => geohash.StartsWith(x.Key)).OrderByDescending(x => x.Key.Length).FirstOrDefault();

        bool countryResolvedFromCache = countryFromCache.Key != null;
        int? resolvedCountryId = countryResolvedFromCache ? countryFromCache.Value : null;

        if (!countryResolvedFromCache)
            resolvedCountryId = await SearchCountry(geohash);

        return resolvedCountryId;
    }

    private int? ResolveLocation(int userId, string geohash)
        => _locationCache.Where(x => x.Key.UserId == userId && geohash.StartsWith(x.Key.geohash)).OrderByDescending(x => x.Key.geohash.Length).FirstOrDefault().Value;

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

        var cityGeohash = await sqlConnection.QueryFirstOrDefaultAsync<CityGeohash>(
            "SELECT TOP 1 CityId, Geohash FROM Common.CityGeohash WHERE Geohash IN @Geohashes",
            new { Geohashes = superGeohashes });

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

        var countryGeohash = await sqlConnection.QueryFirstOrDefaultAsync<CountryGeohash>(
            "SELECT TOP 1 CountryId, Geohash FROM Common.CountryGeohash WHERE Geohash IN @Geohashes",
            new { Geohashes = superGeohashes });

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
            string match = await sqlConnection.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 Geohash FROM Common.CityGeohash WHERE CHARINDEX(@Geohash, Geohash) = 1",
                new { Geohash = searchGeohash });

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
            string match = await sqlConnection.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 Geohash FROM Common.CountryGeohash WHERE CHARINDEX(@Geohash, Geohash) = 1",
                new { Geohash = searchGeohash });

            if (match == null)
                return searchGeohash;
        }

        return geohash;
    }

    private SqlConnection GetSqlConnection()
        => new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN"));
}
