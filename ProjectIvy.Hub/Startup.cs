using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProjectIvy.Hub.Enrichers;
using ProjectIvy.Hub.Hubs;
using ProjectIvy.Hub.Services;
using Serilog;

namespace ProjectIvy.Hub;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSignalR();
        services.AddCors(options => options.AddPolicy("CorsPolicy", builder =>
        {
            builder.AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials()
                   .SetIsOriginAllowed(host => true);
        }))
        .AddSingleton<TrackingProcessingService>()
        .AddHostedService(provider => provider.GetRequiredService<TrackingProcessingService>())
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
           .UseSerilogRequestLogging(configure =>
            {
                configure.EnrichDiagnosticContext = (context, httpContext) =>
                {
                    foreach (var header in httpContext.Request.Headers)
                    {
                        context.Set($"header_{header.Key.Replace("-", "")}", header.Value.ToString());
                    }
                };
            });
    }
}
