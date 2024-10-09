using System.Net;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Filters;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[PacketFilter]
public class PaymentsController : Controller
{
    private readonly ILogger<PaymentsController> _logger;
    private readonly string _bankPaymentsEndpoint;
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly PaymentsRepository _paymentsRepository;

    public PaymentsController(ILogger<PaymentsController> logger, IConfiguration configuration, PaymentsRepository paymentsRepository)
    {
        _logger = logger;
        _bankPaymentsEndpoint = configuration.GetValue<string>("BankPaymentsEndpoint") ?? throw new Exception("Bank payments endpoint required.");
        _paymentsRepository = paymentsRepository;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<PostPaymentResponse?> GetPayment(Guid id)
    {
        return _paymentsRepository.Get(id) is PostPaymentResponse payment ? Ok(payment) : NotFound();
    }

    [HttpPost]
    [SuppressModelStateInvalidFilter]
    public async Task<ActionResult<PostPaymentResponse?>> PostPaymentAsync([FromBody] PostPaymentRequest postPaymentRequest, CancellationToken cancellationToken)
    {
        var payment = new PostPaymentResponse()
        {
            Id = Guid.NewGuid(),
            Status = Models.PaymentStatus.Rejected, // assume rejected by default until we hit the bank api
            CardNumberLastFour = postPaymentRequest.CardNumber[^4..],
            ExpiryMonth = postPaymentRequest.ExpiryMonth,
            ExpiryYear = postPaymentRequest.ExpiryYear,
            Currency = postPaymentRequest.Currency,
            Amount = postPaymentRequest.Amount
        };

        try
        {
            if (ModelState.IsValid)
            {
                var bankPaymentRequest = new BankPaymentRequest()
                {
                    CardNumber = postPaymentRequest.CardNumber,
                    ExpiryDate = $"{postPaymentRequest.ExpiryMonth.ToString("D2")}/{postPaymentRequest.ExpiryYear}",
                    Currency = postPaymentRequest.Currency,
                    Amount = postPaymentRequest.Amount,
                    Cvv = postPaymentRequest.Cvv
                };

                var bankPaymentsResponse = await _httpClient.PostAsJsonAsync(_bankPaymentsEndpoint, bankPaymentRequest, cancellationToken);
                var bankPaymentsStatusCode = (int)bankPaymentsResponse.StatusCode;

                payment.Status = Models.PaymentStatus.Declined; // assume declined because we have hit the bank api

                if (bankPaymentsResponse.IsSuccessStatusCode
                    && await bankPaymentsResponse.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken) is BankPaymentResponse responseContent)
                {
                    if (responseContent.Authorized)
                    {
                        payment.Status = Models.PaymentStatus.Authorized;
                    }

                    return Ok(payment);
                }
                else if (bankPaymentsStatusCode == 400)
                {
                    // an unaccepted card detail was sent
                    // refuse with 403 - Forbidden so that the client knows not to retry
                    return StatusCode((int)HttpStatusCode.Forbidden, payment);
                }
                else if (bankPaymentsStatusCode >= 500)
                {
                    // something went wrong at the bank
                    // return a 502 - Bad Gateway
                    return StatusCode((int)HttpStatusCode.BadGateway, payment);
                }
            }

            return BadRequest(payment);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, payment); // client closed request
        }
        catch (Exception e)
        {
            // our error
            string message = $"An error occurred. Payment ID: {payment.Id}";
            _logger.LogError(e, message);

            return StatusCode((int)HttpStatusCode.InternalServerError, payment);
        }
        finally
        {
            // even failures should be stored for record purposes
            payment.Timestamp = DateTimeOffset.UtcNow;
            _paymentsRepository.Add(payment);
        }
    }
}