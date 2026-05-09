namespace ChatCRM.Application.Agents.Exceptions
{
    /// <summary>
    /// Thrown when agent input fails validation (duplicate name, missing fields, blank model).
    /// Carries field-level errors as translation keys for the UI to localise.
    /// </summary>
    public sealed class AgentValidationException : Exception
    {
        public IReadOnlyDictionary<string, string> Errors { get; }

        public AgentValidationException(IReadOnlyDictionary<string, string> errors)
            : base($"Agent validation failed ({errors.Count} issue(s)).")
        {
            Errors = errors;
        }
    }

    /// <summary>
    /// Thrown when an operation would violate the workspace's "exactly one default" /
    /// "default must be active" / "can't delete default" invariants.
    /// </summary>
    public sealed class AgentInvariantException : Exception
    {
        public string Code { get; }
        public AgentInvariantException(string code, string message) : base(message)
        {
            Code = code;
        }

        public static AgentInvariantException DefaultMustBeActive() =>
            new("default_must_be_active", "The default agent must remain active. Promote another agent to default before deactivating this one.");

        public static AgentInvariantException DefaultMustExistFirst() =>
            new("default_must_exist", "Promote another agent to default before deleting the current default.");

        public static AgentInvariantException InactiveCannotBeDefault() =>
            new("inactive_cannot_be_default", "An inactive agent cannot be the workspace default. Activate it first.");
    }
}
