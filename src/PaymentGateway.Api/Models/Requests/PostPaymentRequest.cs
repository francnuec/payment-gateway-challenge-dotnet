using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using PaymentGateway.Api.Validators;

namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest
{
    [Required]
    [Length(14, 19)]
    [RegularExpression(@"^\d+$", ErrorMessage = "Only numbers allowed.")]
    [JsonPropertyName("card_number")]
    public string CardNumber { get; set; }

    [Required]
    [Range(1, 12)]
    [JsonPropertyName("expiry_month")]
    public int ExpiryMonth { get; set; }

    [Required]
    [FutureDate(MonthProperty = nameof(ExpiryMonth))]
    [JsonPropertyName("expiry_year")]
    public int ExpiryYear { get; set; }

    [Required]
    [Length(3, 3)]
    [AllowedValues("EUR", "GBP", "USD", ErrorMessage = "Only EUR, GBP, or USD allowed.")]
    [JsonPropertyName("currency")]
    public string Currency { get; set; }

    [Required]
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [Required]
    [Length(3, 4)]
    [RegularExpression(@"^\d+$", ErrorMessage = "Only numbers allowed.")]
    [JsonPropertyName("cvv")]
    public string Cvv { get; set; }
}