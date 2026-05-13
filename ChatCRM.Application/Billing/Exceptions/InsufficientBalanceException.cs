namespace ChatCRM.Application.Billing.Exceptions
{
    /// <summary>
    /// Thrown by the billing gate when the wallet doesn't have enough funds to cover an outgoing
    /// message. The send is aborted before any Meta API call so we never race a charge against
    /// a delivery failure. Carries the quoted price + current balance so the caller can render
    /// a clear "Top up to continue" message.
    /// </summary>
    public sealed class InsufficientBalanceException : Exception
    {
        public decimal CurrentBalanceUsd { get; }
        public decimal RequiredUsd       { get; }

        public InsufficientBalanceException(decimal currentBalanceUsd, decimal requiredUsd)
            : base($"Insufficient wallet balance: have ${currentBalanceUsd:0.0000}, need ${requiredUsd:0.0000}.")
        {
            CurrentBalanceUsd = currentBalanceUsd;
            RequiredUsd       = requiredUsd;
        }
    }
}
