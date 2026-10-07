using BankLedger.Application.DTOs;
using BankLedger.Domain.UseCases;
using BankLedger.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace BankLedger.Api.Controllers;

[ApiController]
[Route("balances")]
public sealed class BalancesController : ControllerBase
{
    private readonly GetBalanceUseCase _getBalanceUseCase;

    public BalancesController(GetBalanceUseCase getBalanceUseCase)
    {
        _getBalanceUseCase = getBalanceUseCase;
    }

    [HttpGet]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime? from,
        CancellationToken cancellationToken)
    {
        var accountNumber = HttpContext.Items["AccountNumber"]?.ToString();

        if (string.IsNullOrEmpty(accountNumber))
        {
            return Unauthorized("Account not authenticated");
        }

        var balance = await _getBalanceUseCase.ExecuteAsync(accountNumber, from, cancellationToken);

        var response = new BalanceResponse
        {
            AccountNumber = accountNumber,
            Balance = balance.Amount,
            AsOf = from ?? DateTime.UtcNow
        };

        return Ok(response);
    }
}