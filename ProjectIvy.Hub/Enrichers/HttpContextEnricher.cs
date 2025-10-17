using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace ProjectIvy.Hub.Enrichers;

public class HttpContextEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return;

        // Enrich with request information
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestMethod", httpContext.Request.Method));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestPath", httpContext.Request.Path));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestQueryString", httpContext.Request.QueryString.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestScheme", httpContext.Request.Scheme));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestHost", httpContext.Request.Host.ToString()));
        
        // Enrich with client information
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientIp", httpContext.Request.Headers["CF-Connecting-IP"].ToString()));
    
        // Enrich with all request headers
        foreach (var header in httpContext.Request.Headers)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty($"Header_{header.Key.Replace("-", "")}", header.Value.ToString()));
        }
        
        // Enrich with trace/correlation identifiers
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceIdentifier", httpContext.TraceIdentifier));
        
        if (httpContext.Request.Headers.ContainsKey("X-Correlation-ID"))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", httpContext.Request.Headers["X-Correlation-ID"].ToString()));
        }
    }
}
