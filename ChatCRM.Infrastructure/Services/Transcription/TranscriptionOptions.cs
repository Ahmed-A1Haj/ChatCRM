namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// Bound from the <c>Transcription</c> config section. Keys/secrets live in
    /// <c>appsettings.Development.json</c> or environment variables (<c>Transcription__*</c>),
    /// never in committed config.
    /// </summary>
    public sealed class TranscriptionOptions
    {
        /// <summary>Active driver: <c>groq</c> | <c>whisperapi</c> | <c>local</c> | <c>mock</c>.</summary>
        public string Driver { get; set; } = "mock";

        /// <summary>Max transcription attempts before a message is marked permanently failed.</summary>
        public int MaxAttempts { get; set; } = 4;

        /// <summary>How often the background worker polls for pending audio (seconds).</summary>
        public int PollSeconds { get; set; } = 10;

        public GroqOptions Groq { get; set; } = new();
        public WhisperApiOptions WhisperApi { get; set; } = new();
        public LocalWhisperOptions Local { get; set; } = new();

        public sealed class GroqOptions
        {
            public string? ApiKey { get; set; }
            public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
            public string Model { get; set; } = "whisper-large-v3";
        }

        public sealed class WhisperApiOptions
        {
            public string? ApiKey { get; set; }
            public string Url { get; set; } = "https://api.whisper-api.com/transcribe";
            public string ModelSize { get; set; } = "base";
        }

        public sealed class LocalWhisperOptions
        {
            /// <summary>HTTP endpoint of a self-hosted faster-whisper / whisper.cpp server.</summary>
            public string Url { get; set; } = "http://localhost:9000/transcribe";
        }
    }
}
