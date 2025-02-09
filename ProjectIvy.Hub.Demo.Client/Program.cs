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
            var connection = new HubConnectionBuilder().WithUrl("http://localhost:8080/TrackingHub").Build();
            connection.On<Tracking>(TrackingEvents.Receive, message => Console.WriteLine(message.Latitude));
            await connection.StartAsync();

            double lat = 45.79841581236269;
            double lng = 15.912266106962798;
            double spd = 10;

            while(true)
            {
                var t = new Tracking()
                {
                    Latitude = lat,
                    Longitude = lng,
                    Speed = spd,
                    Timestamp = DateTime.Now,
                    UserId = 1002
                };

                lat = lat + 0.100;
                //lng = lng - 0.0001;
                spd++;

                await connection.SendAsync("Send", t);

                Console.ReadLine();
            }
        }
    }
}
