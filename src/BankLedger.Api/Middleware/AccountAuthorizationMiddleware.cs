namespace BankLedger.Api.Middleware;

public sealed class AccountAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private const string AuthorizationHeaderName = "Authorization";

    public AccountAuthorizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(AuthorizationHeaderName, out var authHeader) || string.IsNullOrWhiteSpace(authHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing Authorization header");
            return;
        }

        var accountNumber = authHeader.ToString().Trim();

        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid Authorization header");
            return;
        }

        context.Items["AccountNumber"] = accountNumber;

        await _next(context);
    }
}

public static class AccountAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseAccountAuthorization(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AccountAuthorizationMiddleware>();
    }
}