namespace ChatCRM.MVC.Services
{
    public class ProfileImageStorageResult
    {
        public bool Succeeded { get; init; }

        public string? RelativePath { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
