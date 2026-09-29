using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;

namespace ProjectIvy.Hub.Demo.Client;

public static class Program
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task Main()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HUB_BASE_URL")?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:5000";

        var token = Environment.GetEnvironmentVariable("ACCESS_TOKEN");
        HubConnection jobHub = null;
        HubConnection trackingHub = null;

        var sample = new Tracking
        {
            Latitude = 45.79841581236269,
            Longitude = 15.912266106962798,
            Speed = 10,
            Timestamp = DateTime.Now,
            UserId = 1002
        };

        try
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("What do you want to do?");
                Console.WriteLine("1) JobHub.ProcessDay(from, to, reprocess)");
                Console.WriteLine("2) TrackingHub.Send(tracking)");
                Console.WriteLine("3) Test hub connection");
                Console.WriteLine("q) Quit");
                var choice = Prompt(">");

                try
                {
                    switch (choice?.ToLowerInvariant())
                    {
                        case "1":
                            jobHub ??= await Connect($"{baseUrl}/JobHub", token);
                            await InvokeProcessDay(jobHub);
                            break;
                        case "2":
                            if (string.IsNullOrWhiteSpace(token))
                                token = Prompt("Access token (required for TrackingHub.Send)");
                            trackingHub ??= await Connect($"{baseUrl}/TrackingHub", token);
                            sample = await InvokeSend(trackingHub, sample);
                            break;
                        case "3":
                            var connected = await TestConnection(baseUrl, token, jobHub, trackingHub);
                            jobHub = connected.JobHub;
                            trackingHub = connected.TrackingHub;
                            if (!connected.Ok)
                                break;

                            Console.WriteLine("q) Quit");
                            while (true)
                            {
                                var next = Prompt(">");
                                if (next is "q" or "quit" or "exit")
                                    return;
                            }
                        case "q":
                        case "quit":
                        case "exit":
                            return;
                        default:
                            Console.WriteLine("Unknown command.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed: {ex.Message}");
                }
            }
        }
        finally
        {
            if (jobHub is not null)
                await jobHub.DisposeAsync();
            if (trackingHub is not null)
                await trackingHub.DisposeAsync();
        }
    }

    private static async Task<HubConnection> Connect(string url, string token)
    {
        var connection = BuildConnection(url, token);
        if (url.EndsWith("/TrackingHub", StringComparison.OrdinalIgnoreCase))
        {
            connection.On<Tracking>(TrackingEvents.Receive, tracking =>
            {
                Console.WriteLine();
                Console.WriteLine($"[TrackingHub.{TrackingEvents.Receive}] {FormatTracking(tracking)}");
                Console.Write("> ");
            });
        }

        Console.WriteLine($"Connecting to {url}...");
        await connection.StartAsync();
        Console.WriteLine($"Connected ({connection.State}), connection id {connection.ConnectionId}.");
        return connection;
    }

    private static async Task<(bool Ok, HubConnection JobHub, HubConnection TrackingHub)> TestConnection(
        string baseUrl,
        string token,
        HubConnection jobHub,
        HubConnection trackingHub)
    {
        Console.WriteLine("1) JobHub");
        Console.WriteLine("2) TrackingHub");
        var hub = Prompt("Hub");

        switch (hub)
        {
            case "1":
                if (jobHub is not null)
                    await jobHub.DisposeAsync();
                jobHub = await Connect($"{baseUrl}/JobHub", token);
                return (true, jobHub, trackingHub);
            case "2":
                if (trackingHub is not null)
                    await trackingHub.DisposeAsync();
                trackingHub = await Connect($"{baseUrl}/TrackingHub", token);
                return (true, jobHub, trackingHub);
            default:
                Console.WriteLine("Unknown hub.");
                return (false, jobHub, trackingHub);
        }
    }

    private static async Task InvokeProcessDay(HubConnection jobHub)
    {
        var from = ReadDateTime("from", DateTime.Today);
        var to = ReadDateTime("to (exclusive)", DateTime.Today.AddDays(1));
        var reprocess = ReadBool("reprocess", false);

        Console.WriteLine($"Calling ProcessDay({from:O}, {to:O}, {reprocess})...");
        await jobHub.InvokeAsync("ProcessDay", from, to, reprocess);
        Console.WriteLine("ProcessDay completed.");
    }

    private static async Task<Tracking> InvokeSend(HubConnection trackingHub, Tracking previous)
    {
        var tracking = new Tracking
        {
            Accuracy = ReadNullableDouble("accuracy", previous.Accuracy),
            Altitude = ReadNullableDouble("altitude", previous.Altitude),
            Latitude = ReadDouble("latitude", previous.Latitude),
            Longitude = ReadDouble("longitude", previous.Longitude),
            Speed = ReadNullableDouble("speed", previous.Speed),
            Timestamp = ReadDateTime("timestamp", DateTime.Now),
            UserId = ReadInt("userId", previous.UserId)
        };

        Console.WriteLine($"Calling Send({FormatTracking(tracking)})...");
        await trackingHub.InvokeAsync("Send", tracking);
        Console.WriteLine("Send completed.");

        return new Tracking
        {
            Accuracy = tracking.Accuracy,
            Altitude = tracking.Altitude,
            Latitude = tracking.Latitude + 0.1,
            Longitude = tracking.Longitude,
            Speed = (tracking.Speed ?? 0) + 1,
            Timestamp = tracking.Timestamp,
            UserId = tracking.UserId
        };
    }

    private static HubConnection BuildConnection(string url, string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                if (!string.IsNullOrWhiteSpace(token))
                    options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .WithAutomaticReconnect()
            .Build();

        var name = url.EndsWith("/JobHub", StringComparison.OrdinalIgnoreCase) ? "JobHub" : "TrackingHub";
        connection.Reconnecting += error =>
        {
            Console.WriteLine($"{name} reconnecting: {error?.Message}");
            return Task.CompletedTask;
        };
        connection.Reconnected += connectionId =>
        {
            Console.WriteLine($"{name} reconnected ({connectionId}).");
            return Task.CompletedTask;
        };
        connection.Closed += error =>
        {
            Console.WriteLine(error is null
                ? $"{name} connection closed."
                : $"{name} connection closed: {error.Message}");
            return Task.CompletedTask;
        };

        return connection;
    }

    private static string FormatTracking(Tracking tracking)
        => $"lat={tracking.Latitude.ToString(Invariant)} lng={tracking.Longitude.ToString(Invariant)} speed={tracking.Speed?.ToString(Invariant) ?? "-"} alt={tracking.Altitude?.ToString(Invariant) ?? "-"} acc={tracking.Accuracy?.ToString(Invariant) ?? "-"} ts={tracking.Timestamp:O} userId={tracking.UserId}";

    private static string Prompt(string label, string defaultValue = null)
    {
        Console.Write(defaultValue is null ? $"{label}: " : $"{label} [{defaultValue}]: ");
        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    private static DateTime ReadDateTime(string label, DateTime defaultValue)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue.ToString("O"));
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.RoundtripKind, out var parsed)
                || DateTime.TryParse(value, Invariant, DateTimeStyles.RoundtripKind, out parsed))
                return parsed;

            Console.WriteLine("Enter a date/time, for example 2026-09-29T00:00:00.");
        }
    }

    private static bool ReadBool(string label, bool defaultValue)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue ? "true" : "false");
            if (bool.TryParse(value, out var parsed))
                return parsed;

            Console.WriteLine("Enter true or false.");
        }
    }

    private static double ReadDouble(string label, double defaultValue)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue.ToString(Invariant));
            if (double.TryParse(value, NumberStyles.Float, Invariant, out var parsed)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return parsed;

            Console.WriteLine("Enter a number.");
        }
    }

    private static double? ReadNullableDouble(string label, double? defaultValue)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue?.ToString(Invariant) ?? "");
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (double.TryParse(value, NumberStyles.Float, Invariant, out var parsed)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return parsed;

            Console.WriteLine("Enter a number, or leave empty.");
        }
    }

    private static int ReadInt(string label, int defaultValue)
    {
        while (true)
        {
            var value = Prompt(label, defaultValue.ToString(Invariant));
            if (int.TryParse(value, NumberStyles.Integer, Invariant, out var parsed))
                return parsed;

            Console.WriteLine("Enter an integer.");
        }
    }
}
