using System.ComponentModel.DataAnnotations;
using System.Globalization;
using BankLedger.Domain.Enums;

namespace BankLedger.Application.DTOs;

public sealed class CreateTransactionRequest
{
    [Required]
    [Range(0.01, 1e6, ErrorMessage = "Amount must be between 0.01 and 1mi")]
    public decimal Amount { get; init; }

    [Required]
    public TransactionType Type { get; init; }
}
