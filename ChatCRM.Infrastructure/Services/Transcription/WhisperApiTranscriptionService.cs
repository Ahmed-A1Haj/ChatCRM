using System.Net.Http.Headers;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// WhisperAPI.com driver. Multipart POST to the configured endpoint with the audio file,
    /// language, and model_size, authenticated via an <c>X-API-Key</c> header from config.
    /// Note: the public service offers only a few free credits before it becomes paid.
    /// </summary>
    public sealed class WhisperApiTranscriptionService : ITranscriptionService
    {
        public const string HttpClientName = "WhisperApi";

        private readonly HttpClient _http;
        private readonly TranscriptionOptions _options;
        private readonly ILogger<WhisperApiTranscriptionService> _logger;

        public WhisperApiTranscriptionService(
            HttpClient http, IOptions<TranscriptionOptions> options,
            ILogger<WhisperApiTranscriptionService> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public string ProviderName => "whisper-api";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.WhisperApi.ApiKey);

        public async Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, string? mimeType, string? language, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("WhisperAPI key is not configured (Transcription:WhisperApi:ApiKey).");

            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(audio);
            fileContent.Headers.ContentType = TranscriptionHttp.FileContentType(mimeType);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio" : fileName);
            form.Add(new StringContent(_options.WhisperApi.ModelSize), "model_size");
            if (!string.IsNullOrWhiteSpace(language))
                form.Add(new StringContent(language), "language");

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.WhisperApi.Url) { Content = form };
            request.Headers.Add("X-API-Key", _options.WhisperApi.ApiKey);

            using var resp = await _http.SendAsync(request, cancellationToken);
            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TRANSCRIBE] WhisperAPI returned {Status}: {Body}", (int)resp.StatusCode, Truncate(payload));
                throw new HttpRequestException($"WhisperAPI transcription failed ({(int)resp.StatusCode}).");
            }

            return TranscriptionResponseParser.Parse(payload, ProviderName);
        }

        private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
    }
}
