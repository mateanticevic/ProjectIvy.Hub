using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;
using ProjectIvy.Hub.Services;

namespace ProjectIvy.Hub.Hubs;

public class JobHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly ILogger _logger;
    private readonly TrackingProcessingService _processingService;
    private readonly IMemoryCache _memoryCache;

    public JobHub(ILogger<TrackingHub> logger, TrackingProcessingService processingService, IMemoryCache memoryCache)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _processingService = processingService;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public async Task ProcessDay(DateTime from, DateTime to, bool reprocess = false)
    {
        const int maxBatchSize = 10000;
        using var sqlConnection = GetSqlConnection();

        try
        {
            long total = await sqlConnection.QuerySingleAsync<long>(
                """
                SELECT COUNT_BIG(1)
                FROM Tracking.Tracking
                WHERE (@Reprocess = 1 OR Processed IS NULL)
                  AND Timestamp >= @From
                  AND Timestamp < @To
                """,
                new { From = from, To = to, Reprocess = reprocess });

            if (total == 0)
            {
                await Clients.Caller.SendAsync(JobEvents.ProcessDayProgress, 100);
                return;
            }

            int batchSize = (int)Math.Min(maxBatchSize, Math.Max(1L, (total + 99) / 100));
            var cursorTimestamp = to;
            var cursorId = long.MaxValue;
            long processed = 0;
            int lastReported = 0;

            await Clients.Caller.SendAsync(JobEvents.ProcessDayProgress, 0);

            while (true)
            {
                var trackings = (await sqlConnection.QueryAsync<TrackingForProcessing>(
                    """
                    SELECT TOP (@BatchSize) Id, Geohash, Timestamp, UserId
                    FROM Tracking.Tracking
                    WHERE (@Reprocess = 1 OR Processed IS NULL)
                      AND Timestamp >= @From
                      AND Timestamp < @To
                      AND (Timestamp < @CursorTimestamp OR (Timestamp = @CursorTimestamp AND Id < @CursorId))
                    ORDER BY Timestamp DESC, Id DESC
                    """,
                    new { BatchSize = batchSize, From = from, To = to, Reprocess = reprocess, CursorTimestamp = cursorTimestamp, CursorId = cursorId })).AsList();

                foreach (var tracking in trackings)
                    _processingService.EnqueueTracking(tracking);

                processed += trackings.Count;
                int progress = (int)Math.Min(100, processed * 100 / total);
                if (progress != lastReported)
                {
                    lastReported = progress;
                    await Clients.Caller.SendAsync(JobEvents.ProcessDayProgress, progress);
                }

                if (trackings.Count < batchSize)
                    break;

                var lastTracking = trackings[trackings.Count - 1];
                cursorTimestamp = lastTracking.Timestamp;
                cursorId = lastTracking.Id;
            }
            _logger.LogInformation("Finished processing tracking records for the day.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tracking records for processing");
        }
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        _logger.LogInformation("Client disconnected");

        if (exception is not null)
            _logger.LogError(exception, "Unexpected disconnect");

        return base.OnDisconnectedAsync(exception);
    }

    private SqlConnection GetSqlConnection()
        => new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN"));
}
