namespace ChatCRM.Application.Billing.DTOs
{
    public sealed class AuditLogQuery
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string? Actor { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public int Page { get; set; } = 0;
        public int PageSize { get; set; } = 50;
    }

    public sealed class AuditLogPage
    {
        public List<AuditLogRowDto> Rows { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public bool HasMore => (Page + 1) * PageSize < TotalCount;
    }

    public sealed class AuditLogRowDto
    {
        public long Id { get; set; }
        public DateTime AtUtc { get; set; }
        public string Actor { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
    }
}
