# Keycloak JWT Authentication Setup

This document describes the Keycloak JWT authentication implementation for ProjectIvy.Hub.

## Features

The implementation supports JWT token authentication from two sources:
1. **Cookie**: Token stored in `AccessToken` cookie
2. **Authorization Header**: Token in the `Authorization: Bearer <token>` header

## Configuration

### 1. Update appsettings.json

Configure your Keycloak settings in `appsettings.json`:

```json
{
  "Keycloak": {
    "realm": "your-realm-name",
    "auth-server-url": "https://your-keycloak-server/auth/",
    "ssl-required": "none",
    "resource": "your-client-id",
    "verify-token-audience": true,
    "credentials": {
      "secret": "your-client-secret"
    },
    "confidential-port": 0
  }
}
```

### 2. Environment Variables (Optional)

You can also configure Keycloak settings using environment variables:
- `Keycloak__realm`
- `Keycloak__auth-server-url`
- `Keycloak__resource`
- `Keycloak__credentials__secret`

## How It Works

### Token Resolution Priority

1. First, the middleware checks for a token in the `AccessToken` cookie
2. If not found in cookie, it checks the `Authorization` header for a Bearer token
3. The token is then validated against Keycloak

### Authentication Flow

```
Request → OnMessageReceived (Extract token) → Token Validation → OnTokenValidated/OnAuthenticationFailed
```

### Request Examples

#### Using Cookie
```bash
curl -X GET https://your-api.com/api/endpoint \
  -H "Cookie: AccessToken=eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9..."
```

#### Using Authorization Header
```bash
curl -X GET https://your-api.com/api/endpoint \
  -H "Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9..."
```

## Logging

The implementation includes comprehensive logging:
- Token source (Cookie or Authorization header) is logged at Debug level
- Successful token validation is logged at Debug level
- Authentication failures are logged at Warning level with exception details
- Tokens are masked in logs (only last 6 characters shown)

## Security Features

1. **Token Masking**: Sensitive tokens are masked in logs to prevent exposure
2. **HTTPS Flexibility**: `RequireHttpsMetadata` is set to `false` for development environments (configure appropriately for production)
3. **ShowPII**: Enabled for debugging purposes - **disable in production**

## Protecting Endpoints

To protect your SignalR hubs and other endpoints, apply the `[Authorize]` attribute:

```csharp
[Authorize]
public class TrackingHub : Hub
{
    // Your hub implementation
}
```

For more granular control, you can use role-based or policy-based authorization:

```csharp
[Authorize(Roles = "admin")]
[Authorize(Policy = "RequireAdminRole")]
```

## Development vs Production

### Development
- `ShowPII` is enabled for detailed error messages
- `RequireHttpsMetadata` is set to `false`

### Production Recommendations
1. Set `IdentityModelEventSource.ShowPII = false`
2. Set `RequireHttpsMetadata = true`
3. Configure `ssl-required` in Keycloak settings appropriately
4. Use environment variables for sensitive configuration
5. Enable proper HTTPS/TLS certificates

## Troubleshooting

### Token Not Being Accepted

1. Check that the token is valid and not expired
2. Verify Keycloak configuration matches your server setup
3. Check logs for authentication failure details
4. Ensure the token is being sent correctly (Cookie name or Bearer header format)

### CORS Issues

The CORS policy is configured to allow credentials:
```csharp
builder.AllowCredentials()
```

Make sure your client is also configured to send credentials with requests.

### Token Validation Errors

Enable detailed logging by setting the log level to Debug:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

## Package Dependencies

The implementation uses the following NuGet packages:
- `Keycloak.AuthServices.Authentication` (v2.5.3)
- `Keycloak.AuthServices.Authorization` (v2.5.3)
- `Microsoft.AspNetCore.Authentication.JwtBearer` (v9.0.0)
- `Microsoft.IdentityModel.Logging` (v8.2.0)

## Additional Resources

- [Keycloak Documentation](https://www.keycloak.org/documentation)
- [ASP.NET Core Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/)
- [JWT Bearer Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn)
