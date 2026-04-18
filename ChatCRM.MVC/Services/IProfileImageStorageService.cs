using Microsoft.AspNetCore.Http;

namespace ChatCRM.MVC.Services
{
    public interface IProfileImageStorageService
    {
        Task<ProfileImageStorageResult> SaveAsync(
            string userId,
            IFormFile file,
            string? currentImagePath,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(string? imagePath);
    }
}
