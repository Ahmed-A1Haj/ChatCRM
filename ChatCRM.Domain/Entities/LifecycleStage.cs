namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Sales lifecycle stage for a contact. Ordered roughly from earliest to latest in
    /// the customer journey, so reports can sort/group sensibly.
    /// </summary>
    public enum LifecycleStage : byte
    {
        NewClient         = 0,
        NotResponding     = 1,
        Interested        = 2,
        Thinking          = 3,
        WantsAMeeting     = 4,
        WaitingForMeeting = 5,
        Discussed         = 6,
        PotentialClient   = 7,
        WillMakePayment   = 8,
        WaitingForContract = 9,
        OurClient         = 10
    }
}
