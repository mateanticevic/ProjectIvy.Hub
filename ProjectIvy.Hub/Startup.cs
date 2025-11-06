using System;
using System.Threading.Tasks;
using Keycloak.AuthServices.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using ProjectIvy.Hub.Hubs;
using ProjectIvy.Hub.Services;
using Serilog;

namespace ProjectIvy.Hub;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        IdentityModelEventSource.ShowPII = true;

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

        // Configure Keycloak JWT Authentication
        services.AddKeycloakWebApiAuthentication(Configuration, options =>
        {
            options.RequireHttpsMetadata = false;
            
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // First, try to get token from Cookie
                    var token = context.Request.Cookies["AccessToken"];
                    
                    // If not in cookie, try Authorization header
                    if (string.IsNullOrEmpty(token))
                    {
                        var authHeader = context.Request.Headers["Authorization"].ToString();
                        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            token = authHeader.Substring("Bearer ".Length).Trim();
                        }
                    }
                    
                    context.Token = token;
                    
                    if (!string.IsNullOrEmpty(token))
                    {
                        Log.Debug("JWT token received from {Source}", 
                            context.Request.Cookies.ContainsKey("AccessToken") ? "Cookie" : "Authorization header");
                    }
                    
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    Log.Debug("JWT token successfully validated for user");
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception != null)
                    {
                        Log.Warning("Authentication failed: {ExceptionType} - {Message}", 
                            context.Exception.GetType().Name, 
                            context.Exception.Message);
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();
    }

    public void Configure(IApplicationBuilder app, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseCors("CorsPolicy");
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(configure =>
        {
            configure.MapHub<TrackingHub>("/TrackingHub");
        });
        app.UseFileServer();
        app.UseSerilogRequestLogging(configure =>
        {
            configure.EnrichDiagnosticContext = (context, httpContext) =>
            {
                // Add masked token to logs
                string authorizationValue = httpContext.Request.Headers["Authorization"];
                string cookieTokenValue = httpContext.Request.Cookies["AccessToken"];
                string token = authorizationValue ?? cookieTokenValue;

                if (token is not null)
                {
                    // Remove "Bearer " prefix if present
                    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = token.Substring("Bearer ".Length).Trim();
                    }
                    
                    string maskedToken = token.Length > 6 ? $"*****{token[^6..]}" : "*****";
                    context.Set("Token", maskedToken);
                }

                foreach (var header in httpContext.Request.Headers)
                {
                    string headerName = header.Key.Replace("-", "");
                    string headerValue = header.Value.ToString();
                    
                    // Mask sensitive headers
                    if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(headerValue))
                    {
                        headerValue = headerValue.Length > 6 ? $"*****{headerValue[^6..]}" : "*****";
                    }
                    else if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(headerValue))
                    {
                        headerValue = "*****";
                    }
                    
                    context.Set($"header_{headerName}", headerValue);
                }
            };
        });
    }
}
