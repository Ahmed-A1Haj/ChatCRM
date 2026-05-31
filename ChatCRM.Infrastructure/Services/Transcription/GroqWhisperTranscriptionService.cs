using System.Net.Http.Headers;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// Groq's free-tier Whisper endpoint (OpenAI-compatible). Multipart POST to
    /// <c>{BaseUrl}/audio/transcriptions</c> with the audio file + <c>whisper-large-v3</c>,
    /// authenticated with a Bearer API key from config. The default free driver.
    /// </summary>
    public sealed class GroqWhisperTranscriptionService : ITranscriptionService
    {
        public const string HttpClientName = "GroqWhisper";

        private readonly HttpClient _http;
        private readonly TranscriptionOptions _options;
        private readonly ILogger<GroqWhisperTranscriptionService> _logger;

        public GroqWhisperTranscriptionService(
            HttpClient http, IOptions<TranscriptionOptions> options,
            ILogger<GroqWhisperTranscriptionService> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public string ProviderName => "groq";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Groq.ApiKey);

        public async Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, string? mimeType, string? language, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Groq API key is not configured (Transcription:Groq:ApiKey).");

            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(audio);
            fileContent.Headers.ContentType = TranscriptionHttp.FileContentType(mimeType);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio" : fileName);
            form.Add(new StringContent(_options.Groq.Model), "model");
            // verbose_json gives us language + duration alongside the text.
            form.Add(new StringContent("verbose_json"), "response_format");
            if (!string.IsNullOrWhiteSpace(language))
                form.Add(new StringContent(language), "language");

            var url = $"{_options.Groq.BaseUrl.TrimEnd('/')}/audio/transcriptions";
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Groq.ApiKey);

            using var resp = await _http.SendAsync(request, cancellationToken);
            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TRANSCRIBE] Groq returned {Status}: {Body}", (int)resp.StatusCode, Truncate(payload));
                throw new HttpRequestException($"Groq transcription failed ({(int)resp.StatusCode}).");
            }

            return TranscriptionResponseParser.Parse(payload, ProviderName);
        }

        private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
    }
}
