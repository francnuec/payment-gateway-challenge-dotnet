using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();
    
    [Fact]
    public async Task GetPayment_Succeeds()
    {
        // Arrange
        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = Models.PaymentStatus.Authorized,
            ExpiryYear = _random.Next(2023, 2030),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            CardNumberLastFour = _random.Next(1111, 9999).ToString(),
            Currency = "GBP",
            Timestamp = DateTimeOffset.UtcNow
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => ((ServiceCollection)services)
                .AddSingleton(paymentsRepository)))
            .CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<Packet<PostPaymentResponse>>();
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotNull(paymentResponse!.Data);
        Assert.NotEqual(Guid.Empty, paymentResponse.Data!.Id);
        Assert.Equal(Models.PaymentStatus.Authorized, paymentResponse.Data.Status);
        Assert.Equal(payment.CardNumberLastFour, paymentResponse.Data.CardNumberLastFour);
        Assert.Equal(payment.Currency, paymentResponse.Data.Currency);
        Assert.Equal(payment.Amount, paymentResponse.Data.Amount);
        Assert.Equal(payment.ExpiryMonth, paymentResponse.Data.ExpiryMonth);
        Assert.Equal(payment.ExpiryYear, paymentResponse.Data.ExpiryYear);
        Assert.Equal(payment.Timestamp, paymentResponse.Data.Timestamp);
    }

    [Fact]
    public async Task GetPayment_WithFakeId_Returns404NotFound()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();
        
        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostPayment_Authorized()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        var paymentRequest = new PostPaymentRequest()
        {
            Amount = 100,
            CardNumber = "2222405343248877",
            Currency = "GBP",
            Cvv = "123",
            ExpiryMonth = 4,
            ExpiryYear = 2025
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Payments/", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<Packet<PostPaymentResponse>>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotNull(paymentResponse!.Data);
        Assert.NotEqual(Guid.Empty, paymentResponse.Data!.Id);
        Assert.Equal(Models.PaymentStatus.Authorized, paymentResponse.Data.Status);
        Assert.Equal(paymentRequest.CardNumber[^4..], paymentResponse.Data.CardNumberLastFour);
        Assert.Equal(paymentRequest.Currency, paymentResponse.Data.Currency);
        Assert.Equal(paymentRequest.Amount, paymentResponse.Data.Amount);
        Assert.Equal(paymentRequest.ExpiryMonth, paymentResponse.Data.ExpiryMonth);
        Assert.Equal(paymentRequest.ExpiryYear, paymentResponse.Data.ExpiryYear);
        Assert.NotEqual(DateTimeOffset.MinValue, paymentResponse.Data.Timestamp);
    }

    [Fact]
    public async Task PostPayment_Declined()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        var paymentRequest = new PostPaymentRequest()
        {
            Amount = 60000,
            CardNumber = "2222405343248112",
            Currency = "USD",
            Cvv = "456",
            ExpiryMonth = 1,
            ExpiryYear = 2026
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Payments/", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<Packet<PostPaymentResponse>>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotNull(paymentResponse!.Data);
        Assert.NotEqual(Guid.Empty, paymentResponse.Data!.Id);
        Assert.Equal(Models.PaymentStatus.Declined, paymentResponse.Data.Status);
        Assert.Equal(paymentRequest.CardNumber[^4..], paymentResponse.Data.CardNumberLastFour);
        Assert.Equal(paymentRequest.Currency, paymentResponse.Data.Currency);
        Assert.Equal(paymentRequest.Amount, paymentResponse.Data.Amount);
        Assert.Equal(paymentRequest.ExpiryMonth, paymentResponse.Data.ExpiryMonth);
        Assert.Equal(paymentRequest.ExpiryYear, paymentResponse.Data.ExpiryYear);
        Assert.NotEqual(DateTimeOffset.MinValue, paymentResponse.Data.Timestamp);
    }

    [Fact]
    public async Task PostPayment_WithUnacceptedCard_DeclinedWith403Forbidden()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        var paymentRequest = new PostPaymentRequest()
        {
            Amount = 600000,
            CardNumber = "1234405343248112",
            Currency = "EUR",
            Cvv = "457",
            ExpiryMonth = 2,
            ExpiryYear = 2027
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Payments/", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<Packet<PostPaymentResponse>>();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotNull(paymentResponse!.Data);
        Assert.NotEqual(Guid.Empty, paymentResponse.Data!.Id);
        Assert.Equal(Models.PaymentStatus.Declined, paymentResponse.Data.Status);
        Assert.Equal(paymentRequest.CardNumber[^4..], paymentResponse.Data.CardNumberLastFour);
        Assert.Equal(paymentRequest.Currency, paymentResponse.Data.Currency);
        Assert.Equal(paymentRequest.Amount, paymentResponse.Data.Amount);
        Assert.Equal(paymentRequest.ExpiryMonth, paymentResponse.Data.ExpiryMonth);
        Assert.Equal(paymentRequest.ExpiryYear, paymentResponse.Data.ExpiryYear);
        Assert.NotEqual(DateTimeOffset.MinValue, paymentResponse.Data.Timestamp);
    }

    [Theory]
    [InlineData("card", "currency", "cvv", 1, 2023)] // all details wrong
    [InlineData("2222", "USD", "456", 1, 2026)] // card number less than 14 chars
    [InlineData("222240534324811234567890", "USD", "456", 1, 2026)] // card number more than 19 chars
    [InlineData("2222405343248112", "USD", "45667", 1, 2026)] // cvv more than 3 chars
    [InlineData("2222405343248112", "USD", "45", 1, 2026)] // cvv less than 3 chars
    public async Task PostPayment_WithInvalidData_RejectedWith400BadRequest(string cardNumber, string currency, string cvv, int expMonth, int expYear)
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        var paymentRequest = new PostPaymentRequest()
        {
            Amount = 60000,
            CardNumber = cardNumber,
            Currency = currency,
            Cvv = cvv,
            ExpiryMonth = expMonth,
            ExpiryYear = expYear
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Payments/", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<Packet<PostPaymentResponse>>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotNull(paymentResponse!.Data);
        Assert.NotEqual(Guid.Empty, paymentResponse.Data!.Id);
        Assert.Equal(Models.PaymentStatus.Rejected, paymentResponse.Data.Status);
        Assert.Equal(paymentRequest.CardNumber[^4..], paymentResponse.Data.CardNumberLastFour);
        Assert.Equal(paymentRequest.Currency, paymentResponse.Data.Currency);
        Assert.Equal(paymentRequest.Amount, paymentResponse.Data.Amount);
        Assert.Equal(paymentRequest.ExpiryMonth, paymentResponse.Data.ExpiryMonth);
        Assert.Equal(paymentRequest.ExpiryYear, paymentResponse.Data.ExpiryYear);
        Assert.NotEqual(DateTimeOffset.MinValue, paymentResponse.Data.Timestamp);
    }

    [Fact]
    public async Task PostPayment_WithExceptionThrownBeforeBankCall_RejectedWith500InternalServerError()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices((context, services) =>
            {
                context.Configuration["BankPaymentsEndpoint"] = "http://localhost:12345/somewhere"; // change bank endpoint to force an exception
            }))
            .CreateClient();

        var paymentRequest = new PostPaymentRequest()
        {
            Amount = 60000,
            CardNumber = "2222405343248112",
            Currency = "USD",
            Cvv = "456",
            ExpiryMonth = 1,
            ExpiryYear = 2026
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/Payments/", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<Packet<PostPaymentResponse>>();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotNull(paymentResponse!.Data);
        Assert.NotEqual(Guid.Empty, paymentResponse.Data!.Id);
        Assert.Equal(Models.PaymentStatus.Rejected, paymentResponse.Data.Status); // rejected only because the bank endpoint has not been successfully called
        Assert.Equal(paymentRequest.CardNumber[^4..], paymentResponse.Data.CardNumberLastFour);
        Assert.Equal(paymentRequest.Currency, paymentResponse.Data.Currency);
        Assert.Equal(paymentRequest.Amount, paymentResponse.Data.Amount);
        Assert.Equal(paymentRequest.ExpiryMonth, paymentResponse.Data.ExpiryMonth);
        Assert.Equal(paymentRequest.ExpiryYear, paymentResponse.Data.ExpiryYear);
        Assert.NotEqual(DateTimeOffset.MinValue, paymentResponse.Data.Timestamp);
    }
}