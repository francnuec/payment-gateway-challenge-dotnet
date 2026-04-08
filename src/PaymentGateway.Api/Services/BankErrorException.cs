namespace PaymentGateway.Api.Services;

/// <summary>
/// Thrown when the bank returns a non-success HTTP status code. Carries the bank's
/// status code so the controller can map it to the appropriate client-facing response:
///   - Bank 4xx → 400 Bad Request (bank rejected the payment data)
///   - Bank 5xx or other → 502 Bad Gateway (upstream failure)
///
/// The original bank error is logged by PaymentService before throwing.
/// </summary>
public class BankErrorException : Exception
{
    public int StatusCode { get; }

    public BankErrorException(int statusCode)
        : base($"Bank returned HTTP {statusCode}")
    {
        StatusCode = statusCode;
    }
}
