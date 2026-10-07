using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.Exceptions;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.UseCases;

public sealed class CreateTransactionUseCase
{
    private readonly ITransactionRepository _transactionRepository;

    public CreateTransactionUseCase(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<Transaction> ExecuteAsync(
        string accountNumber,
        Money amount,
        TransactionType type,
        Guid idempotencyKey,
        CancellationToken ct)
    {
        var existingTransaction = await _transactionRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (existingTransaction is not null)
        {
            return existingTransaction;
        }

        var currentBalance = await _transactionRepository.GetBalanceDeltaAsync(accountNumber, 0, null, ct);

        if (type == TransactionType.DEBIT)
        {
            var newBalance = currentBalance - amount;
            if (newBalance.Amount < 0)
            {
                throw new InsufficientBalanceException(accountNumber, amount.Amount, currentBalance.Amount);
            }
        }

        var transaction = new Transaction(accountNumber, amount, type, idempotencyKey);
        return await _transactionRepository.CreateAsync(transaction, ct);
    }
}
