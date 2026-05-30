namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// A private, internal-only file attached to a <see cref="WhatsAppContact"/> for team
    /// reference (contracts, IDs, scans, internal notes-as-files, …).
    ///
    /// SECURITY MODEL — these files must NEVER reach the contact:
    /// <list type="bullet">
    /// <item>Stored OUTSIDE wwwroot (App_Data), so the static-file middleware can't serve them.</item>
    /// <item>Reachable only through authenticated + permission-gated controller endpoints that
    /// stream the bytes — there is no public URL.</item>
    /// <item><see cref="IsInternal"/> is always true and is the explicit, queryable marker that
    /// keeps these rows out of any contact-facing view, export, email, or automated message.
    /// No outbound code path (Evolution send, SMTP, CSV export) reads this table.</item>
    /// </list>
    /// </summary>
    public class ContactFile
    {
        public int Id { get; set; }

        public int ContactId { get; set; }
        public WhatsAppContact Contact { get; set; } = null!;

        /// <summary>Display name shown in the admin UI. Renameable; does not affect the stored file.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Opaque on-disk name (GUID + extension). Never derived from user input.</summary>
        public string StoredFileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = "application/octet-stream";

        public long SizeBytes { get; set; }

        /// <summary>
        /// Always true. A clear, persisted flag asserting the file is internal/private and must
        /// be excluded from anything the contact could see or receive. Kept as a column (rather
        /// than implied) so it can be filtered/audited and so the intent is explicit in the data.
        /// </summary>
        public bool IsInternal { get; set; } = true;

        public string? UploadedByUserId { get; set; }
        public User? UploadedByUser { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
