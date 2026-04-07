using System.Collections.Concurrent;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository : IPaymentsRepository
{
    // ConcurrentDictionary is thread-safe for concurrent add/get operations and provides
    // O(1) lookups by payment ID, compared to O(n) with List<T>.FirstOrDefault().
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();

    public void Add(PostPaymentResponse payment)
    {
        _payments[payment.Id] = payment;
    }

    public PostPaymentResponse? Get(Guid id)
    {
        return _payments.GetValueOrDefault(id);
    }
}
