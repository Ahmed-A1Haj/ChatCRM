using ChatCRM.Application.Transcription;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Provider abstraction for speech-to-text. The active concrete driver is chosen by config
    /// (<c>Transcription:Driver</c>) so we can start on a free tier and swap providers without
    /// code changes. Implementations must read credentials from configuration/environment only.
    /// </summary>
    public interface ITranscriptionService
    {
        /// <summary>Stable identifier of this provider (stored on the message, shown in the UI).</summary>
        string ProviderName { get; }

        /// <summary>True when the provider has the configuration it needs (e.g. an API key).</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Transcribes an audio stream. <paramref name="language"/> is an optional hint — when null
        /// the provider auto-detects. Throws on failure so the worker can apply retry/backoff.
        /// </summary>
        Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, string? mimeType, string? language,
            CancellationToken cancellationToken = default);
    }
}
