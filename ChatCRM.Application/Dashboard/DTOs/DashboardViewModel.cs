using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Dashboard.DTOs
{
    /// <summary>
    /// Aggregated read-model that powers the dashboard landing page. Today every property is
    /// populated with mock data inside <see cref="ChatCRM.MVC.Controllers.DashboardController"/>;
    /// individual TODO comments mark each thing that needs a real query.
    /// </summary>
    public sealed class DashboardViewModel
    {
        public DashboardRange Range { get; init; } = DashboardRange.Today;

        public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;
        public string TimeZoneAbbreviation { get; init; } = "UTC";

        public IReadOnlyList<KpiTile> Kpis { get; init; } = [];
        public IReadOnlyList<PipelineStageRow> Pipeline { get; init; } = [];
        public IReadOnlyList<ChannelSlice> Channels { get; init; } = [];
        public IReadOnlyList<AttentionItem> Attention { get; init; } = [];
        public IReadOnlyList<TeamMemberRow> Team { get; init; } = [];
        public IReadOnlyList<ActivityEntry> Activity { get; init; } = [];
    }

    public enum DashboardRange { Today, Week, Month }

    public enum KpiTone { Neutral, Positive, Warning, Danger }

    /// <summary>One numeric tile in the KPI strip.</summary>
    public sealed class KpiTile
    {
        public required string Key { get; init; }       // i18n key suffix, e.g. "OpenConversations"
        public required string LabelKey { get; init; }  // full i18n key for the label
        public required string Value { get; init; }     // formatted display value, e.g. "127" or "12 min"
        public string? DeltaText { get; init; }         // e.g. "+8%" — null hides the delta line
        public KpiTone Tone { get; init; } = KpiTone.Neutral;
        public bool DeltaIsPositive { get; init; }      // drives the arrow direction
    }

    public sealed class PipelineStageRow
    {
        public required LifecycleStage Stage { get; init; }
        public required int Count { get; init; }
        public int Delta { get; init; }                  // signed; +3 means three more than previous period
        public required double Percentage { get; init; } // 0..100; bar fill width
    }

    public sealed class ChannelSlice
    {
        public required ChannelType Channel { get; init; }
        public required int Count { get; init; }
        public required double Percentage { get; init; } // 0..100
    }

    public sealed class AttentionItem
    {
        public required int ConversationId { get; init; }
        public required string ContactName { get; init; }
        public string? AvatarUrl { get; init; }
        public required string Initials { get; init; }
        public required ChannelType Channel { get; init; }
        public required string LastMessagePreview { get; init; }
        public required TimeSpan IdleTime { get; init; }
        public required AttentionSeverity Severity { get; init; }
    }

    public enum AttentionSeverity { Calm, Watching, Urgent }

    public sealed class TeamMemberRow
    {
        public required string Name { get; init; }
        public string? AvatarUrl { get; init; }
        public required string Initials { get; init; }
        public required PresenceStatus Presence { get; init; }
        public required int ActiveConversationCount { get; init; }
        public required DateTime LastActiveUtc { get; init; }
    }

    public enum PresenceStatus { Online, Away, Offline }

    public sealed class ActivityEntry
    {
        public required ActivityType Type { get; init; }
        /// <summary>One short sentence with optional bold spans encoded as HTML strong tags. Pre-localized.</summary>
        public required string DescriptionHtml { get; init; }
        public required DateTime AtUtc { get; init; }
    }

    public enum ActivityType { Assigned, StatusChanged, LifecycleChanged, NewContact, MessageSent, NoteAdded }
}
