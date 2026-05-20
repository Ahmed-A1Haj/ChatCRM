namespace ChatCRM.Application.Chats.DTOs
{
    /// <summary>
    /// Thrown when a new WhatsApp instance would collide with an existing one
    /// (same display name, or a race where two creates raced past the uniqueness check).
    /// </summary>
    public class DuplicateInstanceException : Exception
    {
        public DuplicateInstanceException(string message) : base(message) { }
    }
}
