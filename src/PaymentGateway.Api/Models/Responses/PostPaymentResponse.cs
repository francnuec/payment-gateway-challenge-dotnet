using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

public class PostPaymentResponse : PaymentResponseBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("card_number_last_four")]
    public string CardNumberLastFour { get; set; } = string.Empty;

    [JsonPropertyName("expiry_month")]
    public int ExpiryMonth { get; set; }

    [JsonPropertyName("expiry_year")]
    public int ExpiryYear { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
