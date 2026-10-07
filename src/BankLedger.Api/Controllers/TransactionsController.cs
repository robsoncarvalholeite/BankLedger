using BankLedger.Application.DTOs;
using BankLedger.Domain.UseCases;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using BankLedger.Domain.Exceptions;

namespace BankLedger.Api.Controllers;

[ApiController]
[Route("transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly CreateTransactionUseCase _createTransactionUseCase;

    public TransactionsController(CreateTransactionUseCase createTransactionUseCase)
    {
        _createTransactionUseCase = createTransactionUseCase;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransactionRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var accountNumber = HttpContext.Items["AccountNumber"]?.ToString();

        if (string.IsNullOrEmpty(accountNumber))
        {
            return Unauthorized("Account not authenticated");
        }

        if (idempotencyKey == Guid.Empty)
        {
            return BadRequest("Idempotency-Key header is required");
        }

        try
        {
            await _createTransactionUseCase.ExecuteAsync(
                accountNumber,
                new Money(request.Amount),
                request.Type,
                idempotencyKey,
                cancellationToken);

            return Created("", null);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InsufficientBalanceException ex)
        {
            return UnprocessableEntity(new { error = ex.Message, requested = ex.Requested, available = ex.Available });
        }
        catch (ConcurrencyException)
        {
            return Conflict(new { error = "Concurrency conflict, please retry" });
        }
    }
}
