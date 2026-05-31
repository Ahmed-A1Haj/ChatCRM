using System.Text.Json;
using ChatCRM.Application.Transcription;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// Defensive parser for Whisper-style JSON responses. Different providers vary slightly
    /// ({text}, {transcription}, {result}; {language}/{detected_language}; {duration}), so we
    /// probe the common field names rather than bind to a rigid schema.
    /// </summary>
    internal static class TranscriptionResponseParser
    {
        public static TranscriptionResult Parse(string json, string provider)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var text = FirstString(root, "text", "transcription", "result")
                       ?? throw new InvalidOperationException("Provider response had no transcription text.");

            return new TranscriptionResult
            {
                Text = text.Trim(),
                Language = FirstString(root, "language", "detected_language", "lang"),
                DurationSeconds = FirstNumber(root, "duration", "duration_seconds", "audio_duration"),
                Provider = provider
            };
        }

        private static string? FirstString(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty(name, out var el) &&
                    el.ValueKind == JsonValueKind.String)
                {
                    var v = el.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return null;
        }

        private static double? FirstNumber(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty(name, out var el) &&
                    el.ValueKind == JsonValueKind.Number &&
                    el.TryGetDouble(out var v))
                {
                    return v;
                }
            }
            return null;
        }
    }
}
