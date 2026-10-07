using System.ComponentModel.DataAnnotations;
using BankLedger.Domain.Enums;

namespace BankLedger.Application.DTOs;

public sealed class CreateTransactionRequest
{
    [Required]
    [Range(typeof(decimal), "0.01", "999999999999999999")]
    public decimal Amount { get; init; }

    [Required]
    public TransactionType Type { get; init; }
}