using Microsoft.AspNetCore.SignalR.Client;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;
using System;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Demo.Client
{
    public class Program
    {
        public static async Task Main()
        {
            var connection = new HubConnectionBuilder().WithUrl("http://localhost:58295/TrackingHub").Build();
            connection.On<Tracking>(TrackingEvents.Receive, message => Console.WriteLine(message.Latitude));
            await connection.StartAsync();

            double lat = 45;
            double lng = 16;
            double spd = 10;

            while(true)
            {
                var t = new Tracking()
                {
                    Latitude = lat,
                    Longitude = lng,
                    Speed = spd,
                    Timestamp = DateTime.Now
                };

                lat = lat - 0.0001;
                lng = lat - 0.0001;
                spd++;

                await connection.SendAsync("Send", t);

                Console.ReadLine();
            }
        }
    }
}
