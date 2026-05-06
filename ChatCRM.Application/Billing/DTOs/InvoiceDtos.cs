using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Billing.DTOs
{
    public sealed class InvoiceListItemDto
    {
        public int Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public InvoiceStatus Status { get; set; }
        public decimal TotalChargedUsd { get; set; }
        public decimal TotalToppedUpUsd { get; set; }
        public DateTime? IssuedAt { get; set; }
        public string? IssuedByName { get; set; }
    }

    public sealed class InvoiceDetailDto
    {
        public int Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public InvoiceStatus Status { get; set; }
        public decimal TotalChargedUsd { get; set; }
        public decimal TotalMetaCostUsd { get; set; }
        public decimal TotalToppedUpUsd { get; set; }
        public int PaidMessageCount { get; set; }
        public int FreeMessageCount { get; set; }
        public List<BillingCategoryBreakdown> ByCategory { get; set; } = new();
        public DateTime? IssuedAt { get; set; }
        public string? IssuedByName { get; set; }
    }
}
