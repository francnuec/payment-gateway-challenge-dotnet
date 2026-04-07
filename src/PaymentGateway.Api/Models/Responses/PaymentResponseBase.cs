using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models.Responses;

/// <summary>
/// Base response for all payment outcomes. Rejected and bank-error responses return
/// only this shape (status + errors). Successful outcomes (Authorized/Declined) extend
/// this with payment-specific fields via PostPaymentResponse.
/// </summary>
public class PaymentResponseBase
{
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentStatus Status { get; set; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Errors { get; set; }
}
