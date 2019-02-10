using Microsoft.AspNetCore.SignalR.Client;
using ProjectIvy.Hub.Models;
using System;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Demo.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var connection = new HubConnectionBuilder().WithUrl("http://do.anticevic.net:5001/TrackingHub").Build();
            connection.On<Tracking>("Receive", message => Console.WriteLine(message.Latitude));
            await connection.StartAsync();

            var t = new Tracking()
            {
                Latitude = 40,
                Longitude = 20,
                Timestamp = DateTime.Now
            };

            await connection.SendAsync("Send", t);

            Console.ReadLine();
        }
    }
}
