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

        HttpResponseMessage bankResponse;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            bankResponse = await _httpClient.PostAsJsonAsync("", bankRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Bank unreachable after {BankLatencyMs}ms",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        stopwatch.Stop();

        // Logging bank latency separately enables SLA monitoring and performance dashboards.
        _logger.LogInformation(
            "Bank responded with {BankStatusCode} in {BankLatencyMs}ms",
            (int)bankResponse.StatusCode, stopwatch.ElapsedMilliseconds);

        // Non-success responses from the bank are surfaced to the controller via exception
        // so it can map them to the appropriate HTTP status (400 for bank 4xx, 502 for 5xx).
        // The raw response body is logged here (never exposed to the client) for debugging.
        if (!bankResponse.IsSuccessStatusCode)
        {
            var bankStatusCode = (int)bankResponse.StatusCode;
            var body = await bankResponse.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogError(
                "Bank error ({BankStatusCode}): {BankResponseBody}",
                bankStatusCode, body);

            throw new BankErrorException(bankStatusCode);
        }

        var content = await bankResponse.Content
            .ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);

        var status = content?.Authorized == true
            ? PaymentStatus.Authorized
            : PaymentStatus.Declined;

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
