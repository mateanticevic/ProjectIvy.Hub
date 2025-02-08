using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ProjectIvy.Hub.Hubs;
using Serilog;

namespace ProjectIvy.Hub;

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
        })).AddSingleton<TrackingHub>()
        .AddMemoryCache();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseCors("CorsPolicy")
           .UseRouting()
           .UseEndpoints(configure =>
            {
                configure.MapHub<TrackingHub>("/TrackingHub");
            })
           .UseFileServer()
           .UseSerilogRequestLogging();
    }
}
