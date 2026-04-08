using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Helpers;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Integration tests verify the full HTTP pipeline end-to-end: routing, model binding,
/// validation, service orchestration, repository storage, and JSON serialization.
///
/// These complement unit tests by catching wiring issues between components that unit
/// tests cannot detect (e.g., missing DI registrations, incorrect route templates,
/// serialization mismatches).
///
/// A fake bank handler (BankSimulatorHandler) replaces the real bank API so these
/// tests run without Docker. This is deliberate — integration tests should verify
/// our code's behavior, not the bank simulator's correctness.
/// </summary>
public class PaymentsControllerIntegrationTests
{
    private static HttpClient CreateClient()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Replace the real bank HTTP client with our in-process simulator.
                    services.AddHttpClient<IPaymentService, PaymentService>()
                        .ConfigurePrimaryHttpMessageHandler(() => new BankSimulatorHandler());
                });
            });

        return factory.CreateClient();
    }

    private static PostPaymentRequest CreateValidRequest(string cardNumber = "2222405343248877") => new()
    {
        CardNumber = cardNumber,
        ExpiryMonth = 12,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/Payments — Authorized / Declined (bank returned 200)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostPayment_WithAuthorizedCard_Returns200WithAuthorized()
    {
        // Card ending in 7 (odd) -> bank returns authorized=true

        // Arrange
        var client = CreateClient();
        var request = CreateValidRequest("2222405343248877");

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.Null(payment.Errors); // successful payments have no errors
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal("8877", payment.CardNumberLastFour);
        Assert.Equal(request.Currency, payment.Currency);
        Assert.Equal(request.Amount, payment.Amount);
        Assert.Equal(request.ExpiryMonth, payment.ExpiryMonth);
        Assert.Equal(request.ExpiryYear, payment.ExpiryYear);
    }

    [Fact]
    public async Task PostPayment_WithDeclinedCard_Returns200WithDeclined()
    {
        // Card ending in 2 (even) -> bank returns authorized=false

        // Arrange
        var client = CreateClient();
        var request = CreateValidRequest("2222405343248112");

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
        Assert.Null(payment.Errors);
        Assert.Equal("8112", payment.CardNumberLastFour);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/Payments — Bank error responses (4xx, 5xx)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostPayment_WhenBankReturns5xx_Returns502BadGateway()
    {
        // Card ending in 0 -> bank returns 503. The gateway surfaces this as 502
        // because the failure is upstream — the client should retry later.

        // Arrange
        var client = CreateClient();
        var request = CreateValidRequest("2222405343248870");

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task PostPayment_WhenBankReturns4xx_Returns400WithRejected()
    {
        // Card starting with "0" -> bank returns 400 (test-only simulator rule).
        // The gateway relays this as 400 Bad Request with a Rejected status.

        // Arrange
        var client = CreateClient();
        var request = CreateValidRequest("01234567890123");

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var result = await response.Content.ReadFromJsonAsync<PaymentResponseBase>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(PaymentStatus.Rejected, result!.Status);
        Assert.NotNull(result.Errors);
        Assert.NotEmpty(result.Errors!);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POST /api/Payments — Validation failures (Rejected)
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("12345", "GBP", "123")]           // card number too short (5 chars, min 14)
    [InlineData("12345678901234567890", "GBP", "123")] // card number too long (20 chars, max 19)
    [InlineData("abcdefghijklmn", "GBP", "123")]  // card number contains non-numeric characters
    [InlineData("22224053432488", "XYZ", "123")]   // unsupported currency
    [InlineData("22224053432488", "GBP", "12")]    // CVV too short (2 chars, min 3)
    [InlineData("22224053432488", "GBP", "12345")] // CVV too long (5 chars, max 4)
    [InlineData("22224053432488", "GBP", "abc")]   // CVV contains non-numeric characters
    public async Task PostPayment_WithInvalidData_Returns400WithRejectedAndErrors(
        string cardNumber, string currency, string cvv)
    {
        // Arrange
        var client = CreateClient();
        var request = new PostPaymentRequest
        {
            CardNumber = cardNumber,
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = currency,
            Amount = 100,
            Cvv = cvv
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var result = await response.Content.ReadFromJsonAsync<PaymentResponseBase>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(PaymentStatus.Rejected, result!.Status);
        Assert.NotNull(result.Errors);
        Assert.NotEmpty(result.Errors!);
    }

    [Fact]
    public async Task PostPayment_WithExpiredDate_Returns400WithRejectedAndErrors()
    {
        // Arrange
        var client = CreateClient();
        var request = new PostPaymentRequest
        {
            CardNumber = "2222405343248877",
            ExpiryMonth = 1,
            ExpiryYear = 2020,
            Currency = "GBP",
            Amount = 100,
            Cvv = "123"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var result = await response.Content.ReadFromJsonAsync<PaymentResponseBase>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(PaymentStatus.Rejected, result!.Status);
        Assert.NotNull(result.Errors);
        Assert.NotEmpty(result.Errors!);
    }

    [Fact]
    public async Task PostPayment_RejectedResponse_DoesNotContainPaymentFields()
    {
        // Rejected responses should only have status + errors, not payment-specific
        // fields like id, card_number_last_four, etc. Deserializing as PostPaymentResponse
        // should leave those fields at their defaults.

        // Arrange
        var client = CreateClient();
        var request = new PostPaymentRequest
        {
            CardNumber = "12345",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 100,
            Cvv = "123"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var result = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(result);
        // These fields should be absent from a rejected response (defaults for their types)
        Assert.Equal(Guid.Empty, result!.Id);
        Assert.Equal(string.Empty, result.CardNumberLastFour);
        Assert.Equal(string.Empty, result.Currency);
        Assert.Equal(0, result.Amount);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GET /api/Payments/{id} — Retrieve stored payments
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayment_AfterSuccessfulPost_ReturnsStoredPayment()
    {
        // Round-trip test: POST a payment, then GET it back by ID.
        // Verifies the full write-then-read path through the pipeline.

        // Arrange
        var client = CreateClient();
        var request = CreateValidRequest();
        var postResponse = await client.PostAsJsonAsync("/api/Payments", request);
        var posted = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();
        Assert.NotNull(posted);

        // Act
        var getResponse = await client.GetAsync($"/api/Payments/{posted!.Id}");
        var retrieved = await getResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(retrieved);
        Assert.Equal(posted.Id, retrieved!.Id);
        Assert.Equal(posted.Status, retrieved.Status);
        Assert.Equal(posted.CardNumberLastFour, retrieved.CardNumberLastFour);
        Assert.Equal(posted.ExpiryMonth, retrieved.ExpiryMonth);
        Assert.Equal(posted.ExpiryYear, retrieved.ExpiryYear);
        Assert.Equal(posted.Currency, retrieved.Currency);
        Assert.Equal(posted.Amount, retrieved.Amount);
        Assert.Null(retrieved.Errors); // stored payments have no errors
    }

    [Fact]
    public async Task GetPayment_WithNonExistentId_Returns404()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPayment_WithInvalidGuidFormat_Returns404()
    {
        // The {id:guid} route constraint rejects non-GUID segments before the
        // controller is reached, so the response is 404 Not Found (no route match).

        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/Payments/not-a-guid");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
