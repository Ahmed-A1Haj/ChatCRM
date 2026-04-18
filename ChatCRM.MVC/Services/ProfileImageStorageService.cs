using Microsoft.AspNetCore.Http;

namespace ChatCRM.MVC.Services
{
    public class ProfileImageStorageService : IProfileImageStorageService
    {
        private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

        private const long MaxFileSizeBytes = 2 * 1024 * 1024;

        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ProfileImageStorageService> _logger;

        public ProfileImageStorageService(
            IWebHostEnvironment environment,
            ILogger<ProfileImageStorageService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public async Task<ProfileImageStorageResult> SaveAsync(
            string userId,
            IFormFile file,
            string? currentImagePath,
            CancellationToken cancellationToken = default)
        {
            if (file is null || file.Length == 0)
            {
                return new ProfileImageStorageResult
                {
                    ErrorMessage = "Select an image to upload."
                };
            }

            if (file.Length > MaxFileSizeBytes)
            {
                return new ProfileImageStorageResult
                {
                    ErrorMessage = "Profile images must be 2 MB or smaller."
                };
            }

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedContentTypes.TryGetValue(extension.ToLowerInvariant(), out var expectedContentType))
            {
                return new ProfileImageStorageResult
                {
                    ErrorMessage = "Use a JPG, PNG, or WEBP image."
                };
            }

            if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            {
                return new ProfileImageStorageResult
                {
                    ErrorMessage = "The uploaded file does not look like a supported image type."
                };
            }

            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var uploadsRoot = Path.Combine(webRootPath, "uploads", "profiles");
            var userDirectory = Path.Combine(uploadsRoot, userId);

            Directory.CreateDirectory(userDirectory);

            var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var destinationPath = Path.Combine(userDirectory, fileName);

            await using (var stream = new FileStream(destinationPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            await DeleteAsync(currentImagePath);

            var relativePath = $"/uploads/profiles/{userId}/{fileName}";

            _logger.LogInformation("Stored profile image for user {UserId} at {RelativePath}", userId, relativePath);

            return new ProfileImageStorageResult
            {
                Succeeded = true,
                RelativePath = relativePath
            };
        }

        public Task DeleteAsync(string? imagePath)
        {
            var fullPath = ResolveOwnedImagePath(imagePath);
            if (fullPath is not null && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return Task.CompletedTask;
        }

        private string? ResolveOwnedImagePath(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !imagePath.StartsWith("/uploads/profiles/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = imagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var uploadsRoot = Path.GetFullPath(Path.Combine(webRootPath, "uploads", "profiles"));
            var candidatePath = Path.GetFullPath(Path.Combine(webRootPath, relative));

            if (!candidatePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return candidatePath;
        }
    }
}
