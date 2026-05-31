using System.Net.Http.Headers;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    internal static class TranscriptionHttp
    {
        /// <summary>
        /// Builds a safe multipart file Content-Type. WhatsApp voice notes arrive as
        /// <c>audio/ogg; codecs=opus</c>; <see cref="MediaTypeHeaderValue"/>'s constructor rejects
        /// the parameter, so we strip everything after the ';' and fall back defensively.
        /// </summary>
        public static MediaTypeHeaderValue FileContentType(string? mime)
        {
            var bare = (mime ?? string.Empty).Split(';')[0].Trim();
            if (string.IsNullOrEmpty(bare)) bare = "application/octet-stream";
            try { return new MediaTypeHeaderValue(bare); }
            catch { return new MediaTypeHeaderValue("application/octet-stream"); }
        }
    }
}
