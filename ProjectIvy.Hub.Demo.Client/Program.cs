using Microsoft.AspNetCore.SignalR.Client;
using ProjectIvy.Hub.Models;
using System;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Demo.Client
{
    public class Program
    {
        public static async Task Main()
        {
            var connection = new HubConnectionBuilder().WithUrl("http://do.anticevic.net:5001/TrackingHub").Build();
            connection.On<Tracking>("Receive", message => Console.WriteLine(message.Latitude));
            await connection.StartAsync();

            double lat = 45;
            double lng = 16;

            while(true)
            {
                var t = new Tracking()
                {
                    Latitude = lat,
                    Longitude = lng,
                    Timestamp = DateTime.Now
                };

                lat = lat - 0.0001;
                lng = lat - 0.0001;

                await connection.SendAsync("Send", t);

                Console.ReadLine();
            }
        }
    }
}
