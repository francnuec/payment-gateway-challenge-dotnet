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
    public async Task<IActionResult> PostPayment(
        [FromBody] PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();

            // Structured logging with named parameters enables filtering and alerting
            // in log aggregation tools (e.g., Datadog, Seq, Application Insights).
            _logger.LogWarning("Payment rejected: {ValidationErrors}",
                string.Join("; ", errors));

            // Rejected responses return only status + errors (no payment ID or card details)
            // since the payment was never processed.
            return BadRequest(new PaymentResponseBase
            {
                Status = PaymentStatus.Rejected,
                Errors = errors
            });
        }

        try
        {
            var result = await _paymentService.ProcessPaymentAsync(request, cancellationToken);
            _paymentsRepository.Add(result);
            return Ok(result);
        }
        catch (BankErrorException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            // Bank rejected the request data — already logged by PaymentService.
            return BadRequest(new PaymentResponseBase
            {
                Status = PaymentStatus.Rejected,
                Errors = new List<string> { "One or more fields have invalid values" }
            });
        }
        catch (Exception ex) when (ex is BankErrorException or HttpRequestException)
        {
            // Bank 5xx or network failure — already logged by PaymentService.
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
