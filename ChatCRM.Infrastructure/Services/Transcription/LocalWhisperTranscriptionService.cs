using System.Net.Http.Headers;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// Self-hosted driver for a local faster-whisper / whisper.cpp HTTP server — a fully free,
    /// no-cost setup. Multipart POST of the audio file to a configurable URL; the response is
    /// parsed leniently since local servers vary in their JSON shape.
    /// </summary>
    public sealed class LocalWhisperTranscriptionService : ITranscriptionService
    {
        public const string HttpClientName = "LocalWhisper";

        private readonly HttpClient _http;
        private readonly TranscriptionOptions _options;
        private readonly ILogger<LocalWhisperTranscriptionService> _logger;

        public LocalWhisperTranscriptionService(
            HttpClient http, IOptions<TranscriptionOptions> options,
            ILogger<LocalWhisperTranscriptionService> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public string ProviderName => "local";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Local.Url);

        public async Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, string? mimeType, string? language, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Local whisper URL is not configured (Transcription:Local:Url).");

            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(audio);
            fileContent.Headers.ContentType = TranscriptionHttp.FileContentType(mimeType);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio" : fileName);
            if (!string.IsNullOrWhiteSpace(language))
                form.Add(new StringContent(language), "language");

            using var resp = await _http.PostAsync(_options.Local.Url, form, cancellationToken);
            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TRANSCRIBE] Local whisper returned {Status}: {Body}", (int)resp.StatusCode, Truncate(payload));
                throw new HttpRequestException($"Local whisper transcription failed ({(int)resp.StatusCode}).");
            }

            return TranscriptionResponseParser.Parse(payload, ProviderName);
        }

        private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
    }
}
