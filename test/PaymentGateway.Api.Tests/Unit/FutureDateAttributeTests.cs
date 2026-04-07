using System.ComponentModel.DataAnnotations;

using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests.Unit;

/// <summary>
/// Tests for the custom FutureDate validator ensure edge cases around month/year
/// boundaries are handled correctly. These are pure unit tests — they validate a
/// single component in isolation with no I/O or framework dependencies.
/// </summary>
public class FutureDateAttributeTests
{
    private static bool IsValid(int month, int year)
    {
        var model = new PostPaymentRequest
        {
            CardNumber = "2222405343248877",
            ExpiryMonth = month,
            ExpiryYear = year,
            Currency = "GBP",
            Amount = 100,
            Cvv = "123"
        };

        var context = new ValidationContext(model) { MemberName = nameof(PostPaymentRequest.ExpiryYear) };
        var results = new List<ValidationResult>();
        return Validator.TryValidateProperty(model.ExpiryYear, context, results);
    }

    [Fact]
    public void FutureYear_IsValid()
    {
        Assert.True(IsValid(1, DateTime.Now.Year + 1));
    }

    [Fact]
    public void PastYear_IsInvalid()
    {
        Assert.False(IsValid(12, DateTime.Now.Year - 1));
    }

    [Fact]
    public void CurrentMonthAndYear_IsValid()
    {
        // Cards are valid through the end of the expiry month — a card expiring this
        // month should still be accepted today.
        Assert.True(IsValid(DateTime.Now.Month, DateTime.Now.Year));
    }

    [Fact]
    public void PastMonthCurrentYear_IsInvalid()
    {
        // Skip in January — there is no past month in the current year to test.
        if (DateTime.Now.Month == 1) return;
        Assert.False(IsValid(DateTime.Now.Month - 1, DateTime.Now.Year));
    }

    [Fact]
    public void December_CurrentYear_IsValid()
    {
        // December wraps: the validator constructs January of the following year,
        // which is always in the future relative to any date in the current year.
        Assert.True(IsValid(12, DateTime.Now.Year));
    }
}
