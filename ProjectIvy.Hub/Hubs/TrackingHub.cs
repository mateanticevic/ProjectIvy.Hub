using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using ProjectIvy.Hub.Constants;
using ProjectIvy.Hub.Models;
using System;
using System.Threading.Tasks;

namespace ProjectIvy.Hub.Hubs
{
    public class TrackingHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            using (var sqlConnection = new SqlConnection(Environment.GetEnvironmentVariable("CONNECTION_STRING_MAIN")))
            {
                await sqlConnection.OpenAsync();
                var tracking = await sqlConnection.QueryFirstOrDefaultAsync<Tracking>("SELECT TOP 1 * FROM Tracking.Tracking WHERE UserId = 1 ORDER BY Timestamp DESC");

                if (tracking != null)
                    await Clients.Caller.SendAsync(TrackingEvents.Receive, tracking);
            }

            return;
        }

        public async Task Send(Tracking tracking)
        {
            await Clients.All.SendAsync(TrackingEvents.Receive, tracking);
        }
    }
}
