using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Unit;

/// <summary>
/// Unit tests for the in-memory repository. These verify storage logic in isolation.
/// In a production system backed by a real database, these would be complemented by
/// integration tests against the database to catch issues like constraint violations,
/// connection failures, and migration errors that in-memory stores can't surface.
/// </summary>
public class PaymentsRepositoryTests
{
    private static PostPaymentResponse CreatePayment() => new()
    {
        Id = Guid.NewGuid(),
        Status = PaymentStatus.Authorized,
        CardNumberLastFour = "8877",
        ExpiryMonth = 12,
        ExpiryYear = DateTime.UtcNow.Year + 1,
        Currency = "GBP",
        Amount = 100
    };

    [Fact]
    public void Add_ThenGet_ReturnsSamePayment()
    {
        var repo = new PaymentsRepository();
        var payment = CreatePayment();

        repo.Add(payment);
        var result = repo.Get(payment.Id);

        Assert.NotNull(result);
        Assert.Equal(payment.Id, result!.Id);
        Assert.Equal(payment.Status, result.Status);
    }

    [Fact]
    public void Get_WithUnknownId_ReturnsNull()
    {
        var repo = new PaymentsRepository();
        Assert.Null(repo.Get(Guid.NewGuid()));
    }

    [Fact]
    public async Task ConcurrentAdds_AllPaymentsStoredSafely()
    {
        // Validates thread safety: 100 concurrent writes followed by reads.
        // This would fail with List<T> due to race conditions in internal array resizing.
        var repo = new PaymentsRepository();
        var payments = Enumerable.Range(0, 100).Select(_ => CreatePayment()).ToList();

        await Task.WhenAll(payments.Select(p => Task.Run(() => repo.Add(p))));

        foreach (var payment in payments)
        {
            Assert.NotNull(repo.Get(payment.Id));
        }
    }
}
