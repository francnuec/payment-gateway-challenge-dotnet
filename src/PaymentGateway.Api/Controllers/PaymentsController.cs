using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        IPaymentsRepository paymentsRepository,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _paymentsRepository = paymentsRepository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<PostPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentsRepository.Get(id);
        if (payment is null)
        {
            _logger.LogInformation("Payment {PaymentId} not found", id);
            return NotFound();
        }

        return Ok(payment);
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPayment(
        [FromBody] PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var rejected = BuildRejectedResponse(request);

            // Structured logging with named parameters enables filtering and alerting
            // in log aggregation tools (e.g., Datadog, Seq, Application Insights).
            _logger.LogWarning("Payment {PaymentId} rejected: {ValidationErrors}",
                rejected.Id,
                string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)));

            return BadRequest(rejected);
        }

        try
        {
            var result = await _paymentService.ProcessPaymentAsync(request, cancellationToken);
            _paymentsRepository.Add(result);
            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            // Bank is unreachable or returned a network-level error. Return 502 so the
            // client knows the failure is upstream, not in our validation or logic.
            _logger.LogError(ex, "Bank communication failure while processing payment");
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Builds a Rejected response for invalid requests. Guards against null/short card
    /// numbers since model binding runs before validation — the action still executes
    /// because we suppressed the automatic ModelState invalid filter.
    /// </summary>
    private static PostPaymentResponse BuildRejectedResponse(PostPaymentRequest request) => new()
    {
        Id = Guid.NewGuid(),
        Status = PaymentStatus.Rejected,
        CardNumberLastFour = request.CardNumber?.Length >= 4
            ? request.CardNumber[^4..]
            : request.CardNumber ?? string.Empty,
        ExpiryMonth = request.ExpiryMonth,
        ExpiryYear = request.ExpiryYear,
        Currency = request.Currency ?? string.Empty,
        Amount = request.Amount
    };
}
