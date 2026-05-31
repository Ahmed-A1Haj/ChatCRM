using ChatCRM.Application.Contacts.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// Stores and serves private, internal-only contact files. Bytes live under
    /// <c>{ContentRoot}/App_Data/contact-files/{contactId}/</c> — a directory the static-file
    /// middleware never serves — and are only ever streamed back through the authorized
    /// controller endpoints. See <see cref="ContactFile"/> for the full security rationale.
    /// </summary>
    public class ContactFileService : IContactFileService
    {
        // Allow-list keyed by extension → the content type we store and serve. We deliberately
        // derive the stored content type from the (validated) extension rather than trusting the
        // browser-supplied one, and we never execute or render these files server-side.
        private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".zip"] = "application/zip",
        };

        private const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB per file
        private const int MaxDisplayNameLength = 200;

        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ContactFileService> _logger;

        public ContactFileService(AppDbContext db, IWebHostEnvironment env, ILogger<ContactFileService> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        public async Task<List<ContactFileDto>> ListAsync(int contactId, CancellationToken cancellationToken = default)
        {
            return await _db.ContactFiles
                .Where(f => f.ContactId == contactId)
                .OrderByDescending(f => f.UploadedAt)
                .Select(f => new ContactFileDto
                {
                    Id = f.Id,
                    FileName = f.FileName,
                    ContentType = f.ContentType,
                    SizeBytes = f.SizeBytes,
                    UploadedByName = f.UploadedByUser != null
                        ? (string.IsNullOrWhiteSpace(f.UploadedByUser.FirstName)
                            ? f.UploadedByUser.Email
                            : f.UploadedByUser.FirstName + " " + f.UploadedByUser.LastName)
                        : null,
                    UploadedAtUtc = f.UploadedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ContactFileDto> UploadAsync(
            int contactId, byte[] data, string originalFileName, string contentType,
            string? uploadedByUserId, CancellationToken cancellationToken = default)
        {
            if (data is null || data.Length == 0)
                throw new InvalidOperationException("The file is empty.");
            if (data.Length > MaxFileSizeBytes)
                throw new InvalidOperationException("Files must be 25 MB or smaller.");

            var ext = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedTypes.TryGetValue(ext, out var storedContentType))
                throw new InvalidOperationException("That file type isn't allowed.");

            var contactExists = await _db.WhatsAppContacts.AnyAsync(c => c.Id == contactId, cancellationToken);
            if (!contactExists)
                throw new KeyNotFoundException($"Contact {contactId} not found.");

            var dir = EnsureContactDirectory(contactId);
            var storedName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(dir, storedName);

            await File.WriteAllBytesAsync(fullPath, data, cancellationToken);

            var entity = new ContactFile
            {
                ContactId = contactId,
                FileName = SanitizeDisplayName(originalFileName, ext),
                StoredFileName = storedName,
                ContentType = storedContentType,
                SizeBytes = data.Length,
                IsInternal = true,
                UploadedByUserId = uploadedByUserId,
                UploadedAt = DateTime.UtcNow
            };

            _db.ContactFiles.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Stored private contact file {FileId} ({Stored}) for contact {ContactId}",
                entity.Id, storedName, contactId);

            string? uploaderName = null;
            if (uploadedByUserId is not null)
            {
                var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == uploadedByUserId, cancellationToken);
                uploaderName = u is null ? null
                    : (string.IsNullOrWhiteSpace(u.FirstName) ? u.Email : $"{u.FirstName} {u.LastName}");
            }

            return new ContactFileDto
            {
                Id = entity.Id,
                FileName = entity.FileName,
                ContentType = entity.ContentType,
                SizeBytes = entity.SizeBytes,
                UploadedByName = uploaderName,
                UploadedAtUtc = entity.UploadedAt
            };
        }

        public async Task<ContactFileContent?> OpenAsync(int contactId, int fileId, CancellationToken cancellationToken = default)
        {
            var file = await _db.ContactFiles
                .FirstOrDefaultAsync(f => f.Id == fileId && f.ContactId == contactId, cancellationToken);
            if (file is null) return null;

            var path = ResolveOwnedPath(contactId, file.StoredFileName);
            if (path is null || !File.Exists(path))
            {
                _logger.LogWarning("Contact file {FileId} row exists but bytes are missing on disk", fileId);
                return null;
            }

            // FileShare.Read so concurrent downloads don't lock each other; the FileResult disposes it.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            return new ContactFileContent
            {
                Stream = stream,
                ContentType = file.ContentType,
                DownloadName = file.FileName
            };
        }

        public async Task<ContactFileDto?> RenameAsync(int contactId, int fileId, string newName, CancellationToken cancellationToken = default)
        {
            var file = await _db.ContactFiles
                .Include(f => f.UploadedByUser)
                .FirstOrDefaultAsync(f => f.Id == fileId && f.ContactId == contactId, cancellationToken);
            if (file is null) return null;

            var ext = Path.GetExtension(file.StoredFileName);
            file.FileName = SanitizeDisplayName(newName, ext);
            await _db.SaveChangesAsync(cancellationToken);

            return new ContactFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                UploadedByName = file.UploadedByUser != null
                    ? (string.IsNullOrWhiteSpace(file.UploadedByUser.FirstName)
                        ? file.UploadedByUser.Email
                        : $"{file.UploadedByUser.FirstName} {file.UploadedByUser.LastName}")
                    : null,
                UploadedAtUtc = file.UploadedAt
            };
        }

        public async Task<bool> DeleteAsync(int contactId, int fileId, CancellationToken cancellationToken = default)
        {
            var file = await _db.ContactFiles
                .FirstOrDefaultAsync(f => f.Id == fileId && f.ContactId == contactId, cancellationToken);
            if (file is null) return false;

            var path = ResolveOwnedPath(contactId, file.StoredFileName);
            _db.ContactFiles.Remove(file);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                if (path is not null && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                // The DB row is the source of truth; an orphaned byte file is harmless and logged.
                _logger.LogWarning(ex, "Deleted contact-file row {FileId} but failed to remove bytes on disk", fileId);
            }

            return true;
        }

        // ── Storage helpers ─────────────────────────────────────────────────

        /// <summary>Private root: {ContentRoot}/App_Data/contact-files — NOT under wwwroot.</summary>
        private string PrivateRoot() => Path.Combine(_env.ContentRootPath, "App_Data", "contact-files");

        private string EnsureContactDirectory(int contactId)
        {
            var dir = Path.Combine(PrivateRoot(), contactId.ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Resolves a stored file to an absolute path and proves it stays inside this contact's
        /// private directory — blocks path traversal even if StoredFileName were ever tampered with.
        /// </summary>
        private string? ResolveOwnedPath(int contactId, string storedFileName)
        {
            if (string.IsNullOrWhiteSpace(storedFileName)) return null;

            // Strip any directory components — stored names are bare GUIDs, never paths.
            var leaf = Path.GetFileName(storedFileName);
            var contactDir = Path.GetFullPath(Path.Combine(PrivateRoot(), contactId.ToString()));
            var candidate = Path.GetFullPath(Path.Combine(contactDir, leaf));

            var withSep = contactDir.EndsWith(Path.DirectorySeparatorChar)
                ? contactDir
                : contactDir + Path.DirectorySeparatorChar;
            return candidate.StartsWith(withSep, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }

        private static string SanitizeDisplayName(string? name, string fallbackExt)
        {
            var baseName = Path.GetFileName((name ?? string.Empty).Trim()); // drop any path parts
            // Replace characters that are awkward in a Content-Disposition filename.
            foreach (var c in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(c, '_');
            baseName = baseName.Replace('"', '_').Replace('\\', '_').Replace('/', '_').Trim();

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = $"file{fallbackExt}";

            // Keep a sensible extension so downloads open correctly even after a rename.
            if (string.IsNullOrEmpty(Path.GetExtension(baseName)) && !string.IsNullOrEmpty(fallbackExt))
                baseName += fallbackExt;

            if (baseName.Length > MaxDisplayNameLength)
                baseName = baseName[..MaxDisplayNameLength];

            return baseName;
        }
    }
}
