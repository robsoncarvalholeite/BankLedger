using BankLedger.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BankLedger.Api.Middleware;

public sealed class AccountAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SqliteConnectionFactory _connectionFactory;
    private const string AuthorizationHeaderName = "Authorization";

    public AccountAuthorizationMiddleware(RequestDelegate next, SqliteConnectionFactory connectionFactory)
    {
        _next = next;
        _connectionFactory = connectionFactory;
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

        using var connection = _connectionFactory.CreateConnection();
        var accountIdObj = await connection.ExecuteScalarAsync<string>(
            "SELECT id FROM accounts WHERE number = @Number",
            new { Number = accountNumber });

        if (accountIdObj == null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Account not found");
            return;
        }

        var accountId = Guid.Parse(accountIdObj);

        context.Items["AccountNumber"] = accountNumber;
        context.Items["AccountId"] = accountId;

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