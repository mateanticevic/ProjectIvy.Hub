using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProjectIvy.Hub.Hubs;
using Serilog;

namespace ProjectIvy.Hub
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR();
            services.AddCors(options => options.AddPolicy("CorsPolicy", builder =>
            {
                builder.AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials()
                       .SetIsOriginAllowed(host => true);
            }));
        }
        
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting()
               .UseEndpoints(configure =>
                {
                    configure.MapHub<TrackingHub>("/TrackingHub");
                })
               .UseFileServer()
               .UseCors("CorsPolicy")
               .UseSerilogRequestLogging();
        }
    }
}
