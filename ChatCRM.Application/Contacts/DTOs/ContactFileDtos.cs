namespace ChatCRM.Application.Contacts.DTOs
{
    /// <summary>Metadata for one private contact file, as shown in the internal admin UI.</summary>
    public class ContactFileDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAtUtc { get; set; }
    }

    /// <summary>
    /// A readable stream + headers for an authorized download. The stream is opened from private
    /// storage (App_Data) and must be disposed by the caller (the FileResult does this).
    /// </summary>
    public class ContactFileContent
    {
        public Stream Stream { get; set; } = Stream.Null;
        public string ContentType { get; set; } = "application/octet-stream";
        public string DownloadName { get; set; } = "file";
    }
}
