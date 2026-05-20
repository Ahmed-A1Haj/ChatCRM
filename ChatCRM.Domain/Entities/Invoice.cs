namespace ChatCRM.Domain.Entities
{
    public enum InvoiceStatus : byte
    {
        Draft     = 0,
        Issued    = 1,
        Void      = 2
    }

    /// <summary>
    /// Monthly billing statement summarising the workspace's wallet activity for a calendar
    /// month. Generated on demand from the Billing → Invoices page (no cron in v1) — clicking
    /// "Generate" snapshots that month's totals into a row, renders a PDF on the fly, and
    /// returns it. The invoice number scheme is <c>INV-{YYYY}{MM}-{SEQ:D4}</c> per workspace.
    ///
    /// Storage stays minimal: we keep the totals + line-item breakdown JSON, regenerate the PDF
    /// on download. Avoids invalidating PDFs when our renderer evolves.
    /// </summary>
    public class Invoice
    {
        public int Id { get; set; }
        public int WorkspaceId { get; set; }

        /// <summary>Human-friendly identifier shown to customers. Unique per workspace.</summary>
        public string Number { get; set; } = string.Empty;

        /// <summary>First instant of the calendar month (UTC). Period end is exclusive +1 month.</summary>
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

        public decimal TotalChargedUsd { get; set; }
        public decimal TotalMetaCostUsd { get; set; }
        public decimal TotalToppedUpUsd { get; set; }
        public int PaidMessageCount { get; set; }
        public int FreeMessageCount { get; set; }

        /// <summary>JSON snapshot of category breakdown at issue time — keeps the PDF reproducible.</summary>
        public string? CategoryBreakdownJson { get; set; }

        public string? IssuedByUserId { get; set; }
        public User? IssuedByUser { get; set; }

        public DateTime? IssuedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
