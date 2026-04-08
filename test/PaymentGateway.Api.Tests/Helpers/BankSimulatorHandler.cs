using System.Net;
using System.Net.Http.Json;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests.Helpers;

/// <summary>
/// Replicates the mountebank bank simulator rules for integration tests,
/// eliminating the need for Docker during test execution.
///
/// Rules (from imposters/bank_simulator.ejs, plus a test-only rule for 400):
///   - Card starting with "0"                -> 400 Bad Request (test-only trigger)
///   - Card ending in odd digit (1,3,5,7,9)  -> 200 authorized=true
///   - Card ending in even digit (2,4,6,8)   -> 200 authorized=false
///   - Card ending in 0                      -> 503 Service Unavailable
/// </summary>
public class BankSimulatorHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bankRequest = await request.Content!.ReadFromJsonAsync<BankPaymentRequest>(cancellationToken);
        var cardNumber = bankRequest!.CardNumber;

        // Test-only rule: cards starting with "0" trigger a bank 400 response.
        // The real simulator returns 400 for missing fields, which our service never
        // triggers — this rule lets us test the bank-4xx-to-client-400 mapping.
        if (cardNumber.StartsWith('0'))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error_message = "Invalid card number" })
            };
        }

        var lastDigit = cardNumber[^1];

        if (lastDigit == '0')
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var authorized = (lastDigit - '0') % 2 != 0;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new BankPaymentResponse
            {
                Authorized = authorized,
                AuthorizationCode = authorized ? Guid.NewGuid().ToString() : null
            })
        };
    }
}
