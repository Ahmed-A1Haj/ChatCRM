namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Meta's WhatsApp Cloud API conversation categories (post-July 2025 model). The category
    /// determines how Meta charges us — and therefore how we charge the customer.
    /// </summary>
    public enum MetaMessageCategory : byte
    {
        /// <summary>Free-form replies inside a 24h customer service window. Free as of July 2025.</summary>
        Service        = 0,

        /// <summary>Account updates, order confirmations, post-purchase notifications. Cheap.</summary>
        Utility        = 1,

        /// <summary>OTPs and security codes. Cheap, region-specific.</summary>
        Authentication = 2,

        /// <summary>Promotions, offers, broadcasts. Most expensive category.</summary>
        Marketing      = 3
    }
}
