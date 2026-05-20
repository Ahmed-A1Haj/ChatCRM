using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Templates.DTOs;
using ChatCRM.Application.Templates.Exceptions;
using ChatCRM.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services.Templates
{
    /// <summary>
    /// Direct Meta Graph API implementation of <see cref="IWhatsAppTemplateProvider"/>.
    ///
    /// Endpoints used (relative to <c>{BaseUrl}/{Version}/{WabaId}</c>):
    ///   POST   /message_templates                — submit a new template
    ///   GET    /message_templates?...            — list / look up by name
    ///   GET    /{template_id}                    — single-template fetch (status poll)
    ///   DELETE /message_templates?name={name}    — soft-delete by name
    ///
    /// Auth: bearer token on every request. A 401 (or Graph error <c>code=190</c> on a 400)
    /// is mapped to <see cref="TemplateAuthenticationException"/> so the application service
    /// can flag the WABA and notify platform admins. Transient transport errors bubble up as
    /// <see cref="HttpRequestException"/> for the caller to retry.
    ///
    /// Status mapping (Meta string → our enum) lives in <see cref="MapStatus"/>. Anything we
    /// don't recognise is treated as <see cref="WhatsAppTemplateStatus.Submitted"/> so the
    /// poller keeps watching it; logged at warning level so we know to add a mapping.
    /// </summary>
    public sealed class MetaGraphTemplateProvider : IWhatsAppTemplateProvider
    {
        public const string HttpClientName = "meta-graph";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MetaGraphOptions _options;
        private readonly ILogger<MetaGraphTemplateProvider> _logger;

        public MetaGraphTemplateProvider(
            IHttpClientFactory httpClientFactory,
            IOptions<MetaGraphOptions> options,
            ILogger<MetaGraphTemplateProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.WabaId) &&
            !string.IsNullOrWhiteSpace(_options.AccessToken);

        public async Task<TemplateProviderSubmitResult> SubmitAsync(
            string name, string languageCode, MetaMessageCategory category,
            string body, string? footer, CancellationToken ct = default)
        {
            EnsureConfigured();
            var metaCategory = ToMetaCategory(category); // throws if Service

            var components = new List<object>
            {
                new { type = "BODY", text = body }
            };
            if (!string.IsNullOrWhiteSpace(footer))
                components.Add(new { type = "FOOTER", text = footer });

            var payload = new
            {
                name,
                language = languageCode,
                category = metaCategory,
                components
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseSegment}/message_templates")
            {
                Content = JsonContent.Create(payload)
            };
            using var resp = await SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(resp, ct);
                _logger.LogWarning(
                    "[META-TEMPLATES] Submit rejected for {Name}/{Lang}: {Code} — {Message}",
                    name, languageCode, err.Code, err.Message);
                return new TemplateProviderSubmitResult
                {
                    Success = false,
                    MappedStatus = WhatsAppTemplateStatus.Rejected,
                    RawStatus = "REJECTED",
                    FailureReason = err.Message
                };
            }

            using var doc = await ReadDocumentAsync(resp, ct);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var rawStatus = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "PENDING" : "PENDING";
            var mapped = MapStatus(rawStatus);

            _logger.LogInformation(
                "[META-TEMPLATES] Submitted {Name}/{Lang} → id={Id} status={Status}",
                name, languageCode, id, rawStatus);

            return new TemplateProviderSubmitResult
            {
                Success = true,
                MetaTemplateId = id,
                MappedStatus = mapped,
                RawStatus = rawStatus
            };
        }

        public async Task<TemplateProviderStatus?> GetStatusAsync(string metaTemplateId, CancellationToken ct = default)
        {
            EnsureConfigured();
            if (string.IsNullOrWhiteSpace(metaTemplateId)) return null;

            // Single-template GET goes through /{base}/{version}/{template_id}, NOT /{waba}/...
            var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/{_options.ApiVersion}/{metaTemplateId}?fields=name,language,status,category,quality_score,components,rejected_reason";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null; // deleted in Business Manager

            if (!resp.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(resp, ct);
                throw new HttpRequestException($"Meta status fetch failed for {metaTemplateId}: {err.Message}");
            }

            using var doc = await ReadDocumentAsync(resp, ct);
            var root = doc.RootElement;
            var rawStatus = root.TryGetProperty("status", out var s) ? s.GetString() ?? "PENDING" : "PENDING";
            var reason = root.TryGetProperty("rejected_reason", out var r) ? r.GetString() : null;

            return new TemplateProviderStatus(
                MapStatus(rawStatus),
                rawStatus,
                reason,
                metaTemplateId);
        }

        public async Task<IReadOnlyList<TemplateProviderRemoteRow>> ListAllAsync(CancellationToken ct = default)
        {
            EnsureConfigured();

            // Meta returns up to limit=1000 by default. v1 doesn't bother paging — workspaces
            // hitting that limit can revisit. fields=... keeps the payload small and stable.
            var url = $"{BaseSegment}/message_templates?limit=1000&fields=name,language,status,category,id,rejected_reason,components";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await SendAsync(req, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(resp, ct);
                throw new HttpRequestException($"Meta list-templates failed: {err.Message}");
            }

            using var doc = await ReadDocumentAsync(resp, ct);
            var rows = new List<TemplateProviderRemoteRow>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return rows;

            foreach (var item in data.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var lang = item.TryGetProperty("language", out var l) ? l.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(lang))
                    continue;

                var rawStatus = item.TryGetProperty("status", out var st) ? st.GetString() ?? "PENDING" : "PENDING";
                var rawCat = item.TryGetProperty("category", out var c) ? c.GetString() : null;
                var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                var reason = item.TryGetProperty("rejected_reason", out var rr) ? rr.GetString() : null;

                ExtractBodyAndFooter(item, out var body, out var footer);

                rows.Add(new TemplateProviderRemoteRow(
                    name,
                    lang,
                    MapStatus(rawStatus),
                    rawStatus,
                    id,
                    reason,
                    FromMetaCategory(rawCat),
                    body,
                    footer));
            }

            return rows;
        }

        public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
        {
            EnsureConfigured();
            if (string.IsNullOrWhiteSpace(name)) return false;

            var url = $"{BaseSegment}/message_templates?name={Uri.EscapeDataString(name)}";
            using var req = new HttpRequestMessage(HttpMethod.Delete, url);
            using var resp = await SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.NotFound) return false;

            if (!resp.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(resp, ct);
                _logger.LogWarning("[META-TEMPLATES] Delete failed for {Name}: {Code} — {Message}", name, err.Code, err.Message);
                throw new HttpRequestException($"Meta delete failed: {err.Message}");
            }

            using var doc = await ReadDocumentAsync(resp, ct);
            var success = doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            return success;
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private string BaseSegment =>
            $"{_options.ApiBaseUrl.TrimEnd('/')}/{_options.ApiVersion}/{_options.WabaId}";

        private void EnsureConfigured()
        {
            if (!IsConfigured) throw new TemplateProviderNotConfiguredException();
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            var http = _httpClientFactory.CreateClient(HttpClientName);
            // Honour configured timeout per request — HttpClient default is 100s which is
            // too long for an interactive create-template flow.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));

            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            // Auth failure handling: 401 always, plus 400/403 with Graph code 190 (token
            // revoked / expired). Drain the body before throwing so the message is informative.
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                var raw = await resp.Content.ReadAsStringAsync(ct);
                resp.Dispose();
                throw new TemplateAuthenticationException($"Meta returned 401: {raw}");
            }

            if (resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                var err = await PeekErrorAsync(resp, ct);
                if (err.Code is 190 or 200)
                {
                    resp.Dispose();
                    throw new TemplateAuthenticationException(
                        $"Meta auth error ({err.Code}): {err.Message}", err.Code);
                }
            }

            return resp;
        }

        /// <summary>Read the error body without consuming it — caller still needs it.</summary>
        private static async Task<MetaError> PeekErrorAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            // ReadAsStringAsync buffers the content; the body remains accessible for a later
            // ReadAsStringAsync / ReadFromJsonAsync because HttpContent caches once buffered.
            var raw = await resp.Content.ReadAsStringAsync(ct);
            return ParseError(raw);
        }

        private static async Task<MetaError> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            var raw = await resp.Content.ReadAsStringAsync(ct);
            return ParseError(raw);
        }

        private static MetaError ParseError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new MetaError("Empty error body", null);
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                    var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetInt32()
                        : (int?)null;
                    return new MetaError(msg ?? raw, code);
                }
            }
            catch (JsonException)
            {
                // Non-JSON error response (rare but possible) — surface verbatim.
            }
            return new MetaError(raw, null);
        }

        private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }

        private static WhatsAppTemplateStatus MapStatus(string rawStatus) =>
            (rawStatus ?? string.Empty).ToUpperInvariant() switch
            {
                "PENDING"          => WhatsAppTemplateStatus.Submitted,
                "IN_APPEAL"        => WhatsAppTemplateStatus.Submitted,
                "PENDING_DELETION" => WhatsAppTemplateStatus.Submitted,
                "APPROVED"         => WhatsAppTemplateStatus.Approved,
                "REJECTED"         => WhatsAppTemplateStatus.Rejected,
                "PAUSED"           => WhatsAppTemplateStatus.Paused,
                "DELETED"          => WhatsAppTemplateStatus.Disabled,
                "DISABLED"         => WhatsAppTemplateStatus.Disabled,
                "LIMIT_EXCEEDED"   => WhatsAppTemplateStatus.Stuck,
                _                  => WhatsAppTemplateStatus.Submitted
            };

        private static string ToMetaCategory(MetaMessageCategory category) =>
            category switch
            {
                MetaMessageCategory.Marketing      => "MARKETING",
                MetaMessageCategory.Utility        => "UTILITY",
                MetaMessageCategory.Authentication => "AUTHENTICATION",
                MetaMessageCategory.Service        => throw new ArgumentException(
                    "Service category is for free-form sends inside the 24h window — templates must be Marketing/Utility/Authentication.",
                    nameof(category)),
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown template category.")
            };

        private static MetaMessageCategory? FromMetaCategory(string? raw) =>
            (raw ?? string.Empty).ToUpperInvariant() switch
            {
                "MARKETING"      => MetaMessageCategory.Marketing,
                "UTILITY"        => MetaMessageCategory.Utility,
                "AUTHENTICATION" => MetaMessageCategory.Authentication,
                _                => null
            };

        /// <summary>
        /// Pull the BODY and FOOTER component texts out of a Meta template payload. v1
        /// templates only have body+footer; HEADER and BUTTONS are deliberately not extracted
        /// (we'd lose information on round-trip, which is fine — we don't author them yet).
        /// </summary>
        private static void ExtractBodyAndFooter(JsonElement template, out string? body, out string? footer)
        {
            body = null;
            footer = null;
            if (!template.TryGetProperty("components", out var components) ||
                components.ValueKind != JsonValueKind.Array)
                return;

            foreach (var c in components.EnumerateArray())
            {
                var type = c.TryGetProperty("type", out var t) ? t.GetString() : null;
                var text = c.TryGetProperty("text", out var x) ? x.GetString() : null;
                if (string.IsNullOrEmpty(type)) continue;

                if (string.Equals(type, "BODY", StringComparison.OrdinalIgnoreCase))
                    body = text;
                else if (string.Equals(type, "FOOTER", StringComparison.OrdinalIgnoreCase))
                    footer = text;
            }
        }

        private readonly record struct MetaError(string Message, int? Code);
    }
}
