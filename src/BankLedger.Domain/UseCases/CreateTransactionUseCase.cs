using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.Exceptions;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.UseCases;

public sealed class CreateTransactionUseCase
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IdempotencyLock _idempotencyLock;
    private readonly GetBalanceUseCase _getBalanceUseCase;
    private readonly IConcurrencyStore _concurrencyStore;

    public CreateTransactionUseCase(ITransactionRepository transactionRepository, IdempotencyLock idempotencyLock, GetBalanceUseCase getBalanceUseCase, IConcurrencyStore concurrencyStore)
    {
        _transactionRepository = transactionRepository;
        _idempotencyLock = idempotencyLock;
        _getBalanceUseCase = getBalanceUseCase;
        _concurrencyStore = concurrencyStore;
    }

    public async Task ExecuteAsync(
        Guid accountId,
        Money amount,
        TransactionType type,
        Guid idempotencyKey,
        CancellationToken ct)
    {
        if (await _idempotencyLock.ExistsAsync(idempotencyKey, ct)) return;

        if (TransactionType.DEBIT == type)
        {
            var currentBalance = await _getBalanceUseCase.ExecuteAsync(accountId, null, ct);
            if (currentBalance < amount) throw new InsufficientBalanceException(amount.Amount, currentBalance.Amount);
        }


        var retries = 5;
        do
        {
            try
            {
                long newOccVersion = await _concurrencyStore.NextAsync(accountId.ToString(), ct);

                var transaction = new Transaction(accountId, amount, type, newOccVersion);
                await _transactionRepository.CreateAsync(transaction, ct);

                await _idempotencyLock.AddAsync(idempotencyKey, ct);
                return;
            }
            catch (ConcurrencyException) { }

        } while (retries-- > 0);

        throw new ConcurrencyException($"Failed to create transaction after 5 attempts due to concurrency conflicts.");
    }
}
