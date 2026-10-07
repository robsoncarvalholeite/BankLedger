using BankLedger.Application.DTOs;
using BankLedger.Application.UseCases;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

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
            var transaction = await _createTransactionUseCase.ExecuteAsync(
                accountNumber,
                new Money(request.Amount),
                request.Type,
                idempotencyKey,
                cancellationToken);

            return CreatedAtAction(nameof(Create), new { id = transaction.Id }, new
            {
                id = transaction.Id,
                accountNumber = transaction.AccountNumber,
                amount = transaction.Amount.Amount,
                type = transaction.Type.ToString(),
                createdAt = transaction.CreatedAt,
                idempotencyKey = transaction.IdempotencyKey
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (BankLedger.Domain.Exceptions.InsufficientBalanceException ex)
        {
            return UnprocessableEntity(new { error = ex.Message, requested = ex.Requested, available = ex.Available });
        }
        catch (BankLedger.Domain.Exceptions.ConcurrencyException)
        {
            return Conflict(new { error = "Concurrency conflict, please retry" });
        }
    }
}