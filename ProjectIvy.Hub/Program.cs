using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Graylog;
using Serilog.Sinks.Graylog.Core.Transport;

namespace ProjectIvy.Hub
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
                                                  .MinimumLevel.Override(nameof(Microsoft), LogEventLevel.Information)
                                                  .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                                                  .Enrich.FromLogContext()
                                                  .WriteTo.Console()
                                                  .WriteTo.Graylog(new GraylogSinkOptions()
                                                  {
                                                      Facility = "project-ivy-hub",
                                                      HostnameOrAddress = Environment.GetEnvironmentVariable("GRAYLOG_HOST"),
                                                      Port = Convert.ToInt32(Environment.GetEnvironmentVariable("GRAYLOG_PORT")),
                                                      TransportType = TransportType.Tcp
                                                  })
                                                  .CreateLogger();
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
            .UseSerilog();
    }
}
