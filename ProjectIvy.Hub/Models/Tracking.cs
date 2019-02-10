using System;

namespace ProjectIvy.Hub.Models
{
    public class Tracking
    {
        public double? Accuracy { get; set; }
        public double? Altitude { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Speed { get; set; }
        public DateTime Timestamp { get; set; }
        public int UserId { get; set; }
    }
}
