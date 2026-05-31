using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Transcription;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// No-cost, no-network default driver. Returns a deterministic placeholder so the whole
    /// transcription pipeline (queue → worker → status → UI) is exercisable without API keys or
    /// credits. Swap to a real driver via <c>Transcription:Driver</c> when keys are available.
    /// </summary>
    public sealed class MockTranscriptionService : ITranscriptionService
    {
        public string ProviderName => "mock";

        public bool IsConfigured => true;

        public async Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, string? mimeType, string? language, CancellationToken cancellationToken = default)
        {
            // Drain the stream so size-dependent behavior is realistic, then return a stub.
            long bytes = 0;
            var buffer = new byte[81920];
            int read;
            while ((read = await audio.ReadAsync(buffer, cancellationToken)) > 0)
                bytes += read;

            return new TranscriptionResult
            {
                Text = $"[mock transcription] {bytes:N0} bytes of audio. Configure a real provider " +
                       "(Transcription:Driver = groq) to get actual text.",
                Language = language ?? "en",
                DurationSeconds = null,
                Provider = ProviderName
            };
        }
    }
}
