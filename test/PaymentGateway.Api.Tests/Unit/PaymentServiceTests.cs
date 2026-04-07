using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Helpers;

namespace PaymentGateway.Api.Tests.Unit;

/// <summary>
/// Unit tests for PaymentService verify bank communication logic in isolation.
/// By mocking the HTTP handler, these tests run instantly without Docker or network
/// access — giving fast feedback during development. Compare with integration tests
/// in PaymentsControllerIntegrationTests which verify the full HTTP pipeline.
/// </summary>
public class PaymentServiceTests
{
    private static PostPaymentRequest CreateValidRequest(string cardNumber = "2222405343248877") => new()
    {
        CardNumber = cardNumber,
        ExpiryMonth = 12,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    private static PaymentService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://bank/payments") };
        return new PaymentService(client, NullLogger<PaymentService>.Instance);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankAuthorizes_ReturnsAuthorized()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BankPaymentResponse
                {
                    Authorized = true,
                    AuthorizationCode = Guid.NewGuid().ToString()
                })
            }));

        var service = CreateService(handler);
        var result = await service.ProcessPaymentAsync(CreateValidRequest(), CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankDeclines_ReturnsDeclined()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BankPaymentResponse
                {
                    Authorized = false,
                    AuthorizationCode = null
                })
            }));

        var service = CreateService(handler);
        var result = await service.ProcessPaymentAsync(
            CreateValidRequest("2222405343248112"), CancellationToken.None);

        Assert.Equal(PaymentStatus.Declined, result.Status);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankReturns5xx_ThrowsBankErrorException()
    {
        // Bank server errors (e.g., 503) are surfaced as BankErrorException so the
        // controller can return 502 Bad Gateway to the client.
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BankErrorException>(
            () => service.ProcessPaymentAsync(
                CreateValidRequest("2222405343248870"), CancellationToken.None));

        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankReturns4xx_ThrowsBankErrorException()
    {
        // Bank client errors (e.g., 400) are surfaced as BankErrorException so the
        // controller can return 400 Bad Request to the client.
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BankErrorException>(
            () => service.ProcessPaymentAsync(CreateValidRequest(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ProcessPayment_WhenBankUnreachable_ThrowsHttpRequestException()
    {
        // Network failures propagate to the controller, which returns 502 Bad Gateway.
        // This separation lets the controller decide the HTTP semantics while the service
        // stays focused on business logic.
        var handler = new MockHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var service = CreateService(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.ProcessPaymentAsync(CreateValidRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ProcessPayment_MapsAllRequiredResponseFields()
    {
        // Verify every field required by the spec: id, status, last_four, expiry, currency, amount.
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BankPaymentResponse { Authorized = true })
            }));

        var service = CreateService(handler);
        var request = CreateValidRequest();
        var result = await service.ProcessPaymentAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal("8877", result.CardNumberLastFour);
        Assert.Equal(request.ExpiryMonth, result.ExpiryMonth);
        Assert.Equal(request.ExpiryYear, result.ExpiryYear);
        Assert.Equal(request.Currency, result.Currency);
        Assert.Equal(request.Amount, result.Amount);
    }
}
