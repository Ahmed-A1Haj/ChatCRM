namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Messaging channel a connected account belongs to.
    /// Add new values as we ship support for additional platforms.
    /// </summary>
    public enum ChannelType : byte
    {
        WhatsApp  = 0,
        Instagram = 1,
        Messenger = 2,
        Telegram  = 3,
        Sms       = 4,
        Email     = 5
    }
}
