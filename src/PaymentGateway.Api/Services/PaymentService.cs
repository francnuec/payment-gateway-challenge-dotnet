using System.Diagnostics;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(HttpClient httpClient, ILogger<PaymentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PostPaymentResponse> ProcessPaymentAsync(
        PostPaymentRequest request, CancellationToken cancellationToken)
    {
        var bankRequest = new BankPaymentRequest
        {
            CardNumber = request.CardNumber,
            ExpiryDate = $"{request.ExpiryMonth:D2}/{request.ExpiryYear}",
            Currency = request.Currency,
            Amount = request.Amount,
            Cvv = request.Cvv
        };

        // Structured logging with semantic parameter names enables log-based dashboards
        // and alerting. In a production system this would pair with distributed tracing
        // (e.g., OpenTelemetry) to correlate gateway and bank calls across services.
        _logger.LogInformation(
            "Sending payment to bank for card ending in {CardLastFour}, {Amount} {Currency}",
            request.CardNumber[^4..], request.Amount, request.Currency);

        var stopwatch = Stopwatch.StartNew();
        var bankResponse = await _httpClient.PostAsJsonAsync("", bankRequest, cancellationToken);
        stopwatch.Stop();

        // Logging bank latency separately enables SLA monitoring and performance dashboards.
        _logger.LogInformation(
            "Bank responded with {BankStatusCode} in {BankLatencyMs}ms",
            (int)bankResponse.StatusCode, stopwatch.ElapsedMilliseconds);

        // Default to Declined — the bank must explicitly authorize for any other outcome.
        var status = PaymentStatus.Declined;

        if (bankResponse.IsSuccessStatusCode)
        {
            var content = await bankResponse.Content
                .ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);

            if (content?.Authorized == true)
            {
                status = PaymentStatus.Authorized;
            }
        }

        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = status,
            CardNumberLastFour = request.CardNumber[^4..],
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount
        };

        _logger.LogInformation(
            "Payment {PaymentId} completed with status {PaymentStatus}",
            payment.Id, payment.Status);

        return payment;
    }
}
