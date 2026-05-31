namespace ChatCRM.Application.Transcription
{
    /// <summary>Result of a speech-to-text call, normalized across providers.</summary>
    public class TranscriptionResult
    {
        public string Text { get; set; } = string.Empty;

        /// <summary>Detected or echoed language code (e.g. "en"). Null when the provider didn't report it.</summary>
        public string? Language { get; set; }

        /// <summary>Audio duration in seconds, when the provider reports it.</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>Provider identifier, e.g. "groq", "whisper-api", "local", "mock".</summary>
        public string Provider { get; set; } = string.Empty;
    }

    /// <summary>Read-only transcription payload exposed to the UI for one message.</summary>
    public class MessageTranscriptionDto
    {
        public int MessageId { get; set; }

        /// <summary>Mirrors <see cref="ChatCRM.Domain.Entities.TranscriptionStatus"/>.</summary>
        public byte Status { get; set; }

        public string? Text { get; set; }
        public string? Language { get; set; }
        public string? Provider { get; set; }
        public string? Error { get; set; }
    }
}
