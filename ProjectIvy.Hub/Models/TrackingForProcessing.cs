using System;

namespace ProjectIvy.Hub.Models;

public class TrackingForProcessing
{
    public long Id { get; set; }

    public string Geohash { get; set; }

    public DateTime Timestamp { get; set; }

    public int UserId { get; set; }
}