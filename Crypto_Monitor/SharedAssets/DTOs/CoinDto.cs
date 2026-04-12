using System.ComponentModel.DataAnnotations;

namespace Crypto_Monitor.SharedAssets.DTOs;

/// <summary>
/// Data Transfer Object representing a coin.
/// Includes DataAnnotations for Input Validation (Requirement 1)
/// </summary>
public class CoinDto
{
    [Required]
    public string CoinId { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CoinName { get; set; } = string.Empty;

    [Required]
    public string Ticker { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "Price must be a positive value")]
    public decimal Price { get; set; }
}
