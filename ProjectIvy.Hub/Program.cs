using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectIvy.Hub.Enrichers;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Graylog;
using Serilog.Sinks.Graylog.Core.Transport;

namespace ProjectIvy.Hub;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            })
        .UseSerilog((hostingContext, services, loggerConfiguration) =>
        {
            var httpContextAccessor = services.GetService<IHttpContextAccessor>();
            
            loggerConfiguration
                .MinimumLevel.Debug()
                .MinimumLevel.Override(nameof(Microsoft), LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.With(new HttpContextEnricher(httpContextAccessor))
                .WriteTo.Console()
                .WriteTo.Graylog(new GraylogSinkOptions()
                {
                    Facility = "project-ivy-hub",
                    HostnameOrAddress = Environment.GetEnvironmentVariable("GRAYLOG_HOST"),
                    Port = Convert.ToInt32(Environment.GetEnvironmentVariable("GRAYLOG_PORT")),
                    TransportType = TransportType.Udp
                });
        });
}
