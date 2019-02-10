using Microsoft.AspNetCore.SignalR;
using ProjectIvy.Hub.Models;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Hubs
{
    public class TrackingHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public async Task Send(Tracking tracking)
        {
            await Clients.All.SendAsync("Receive", tracking);
        }
    }
}
