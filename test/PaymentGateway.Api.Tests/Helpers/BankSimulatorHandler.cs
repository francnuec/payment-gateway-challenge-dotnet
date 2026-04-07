using System.Net;
using System.Net.Http.Json;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests.Helpers;

/// <summary>
/// Replicates the mountebank bank simulator rules for integration tests,
/// eliminating the need for Docker during test execution.
///
/// Rules (from imposters/bank_simulator.ejs):
///   - Card ending in odd digit (1,3,5,7,9) -> 200 authorized=true
///   - Card ending in even digit (2,4,6,8)  -> 200 authorized=false
///   - Card ending in 0                     -> 503 Service Unavailable
/// </summary>
public class BankSimulatorHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bankRequest = await request.Content!.ReadFromJsonAsync<BankPaymentRequest>(cancellationToken);
        var lastDigit = bankRequest!.CardNumber[^1];

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
