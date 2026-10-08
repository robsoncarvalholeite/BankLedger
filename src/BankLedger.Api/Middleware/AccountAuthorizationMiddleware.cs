using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace BankLedger.Api.Middleware;

public sealed class AccountAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private const string AuthorizationHeaderName = "Authorization";

    public AccountAuthorizationMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
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

        using var scope = _scopeFactory.CreateScope();
        var accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();

        var account = await accountRepository.GetByNumberAsync(accountNumber, context.RequestAborted);

        if (account == null)
        {
            account = Account.Create(accountNumber);
            await accountRepository.CreateAsync(account, context.RequestAborted);
        }

        context.Items["AccountNumber"] = account.Number;
        context.Items["AccountId"] = account.Id;

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