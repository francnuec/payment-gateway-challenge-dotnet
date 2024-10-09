using System;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Validators
{
    public class FutureDateAttribute : ValidationAttribute
    {
        public FutureDateAttribute()
        {
            ErrorMessage = "The combination of the expiry month and expiry year values must be in the future.";
        }

        public string? MonthProperty { get; set; }

        public override bool RequiresValidationContext => true;

        public override bool IsValid(object? value)
        {
            return value is DateTime date && date > DateTime.Now;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            DateTime? dateTime = value as DateTime?;

            if (dateTime == null)
            {
                int? yearValue = value as int?;
                int? monthValue = 12; // without a month value, we should validate for the entire year

                if (yearValue == null)
                {
                    throw new ArgumentException("Value must be an integer.", nameof(value));
                }

                if (MonthProperty != null)
                {
                    var monthPropertyInfo = validationContext.ObjectInstance.GetType().GetProperty(MonthProperty);
                    if (monthPropertyInfo == null)
                    {
                        throw new InvalidOperationException("Month Property not found.");
                    }

                    monthValue = monthPropertyInfo.GetValue(validationContext.ObjectInstance) as int?;

                    if (monthValue == null)
                    {
                        throw new InvalidOperationException("Month Property value must be an integer.");
                    }

                    if (monthValue < 1 || monthValue > 12)
                    {
                        throw new InvalidOperationException("Month Property value is valid from 1 through 12.");
                    }
                }

                // increase the value of month by 1 to ensure that we include all the days of the month in our validation
                if (++monthValue == 13)
                {
                    yearValue++;
                    monthValue = 1;
                }

                dateTime = new(yearValue.Value, monthValue.Value, 1);
            }

            return base.IsValid(dateTime, validationContext);
        }
    }
}

