namespace ChatCRM.Application.Billing.DTOs
{
    /// <summary>POST body for the top-up picker.</summary>
    public sealed class CreateTopUpDto
    {
        /// <summary>USD. Validated against StripeOptions.MinAmountUsd / MaxAmountUsd.</summary>
        public decimal AmountUsd { get; set; }
    }
}
