using System.ComponentModel.DataAnnotations;
using System.Globalization;
using BankLedger.Domain.Enums;

namespace BankLedger.Application.DTOs;

public sealed class CreateTransactionRequest
{
    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; init; }

    [Required]
    public TransactionType Type { get; init; }
}