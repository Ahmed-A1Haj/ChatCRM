using System.Text;
using System.Text.Json;
using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services
{
    public class WhatsAppInstanceService : IWhatsAppInstanceService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EvolutionOptions _options;
        private readonly IHubContext<ChatHub> _hub;
        private readonly ILogger<WhatsAppInstanceService> _logger;

        public WhatsAppInstanceService(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IOptions<EvolutionOptions> options,
            IHubContext<ChatHub> hub,
            ILogger<WhatsAppInstanceService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _hub = hub;
            _logger = logger;
        }

        public async Task<List<InstanceDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _db.WhatsAppInstances
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new InstanceDto
                {
                    Id = i.Id,
                    InstanceName = i.InstanceName,
                    DisplayName = i.DisplayName,
                    PhoneNumber = i.PhoneNumber,
                    Status = i.Status,
                    CreatedAt = i.CreatedAt,
                    LastConnectedAt = i.LastConnectedAt,
                    ConversationCount = i.Conversations.Count,
                    UnreadCount = i.Conversations.Sum(c => c.UnreadCount)
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<InstanceDto?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _db.WhatsAppInstances
                .Where(i => i.Id == id)
                .Select(i => new InstanceDto
                {
                    Id = i.Id,
                    InstanceName = i.InstanceName,
                    DisplayName = i.DisplayName,
                    PhoneNumber = i.PhoneNumber,
                    Status = i.Status,
                    CreatedAt = i.CreatedAt,
                    LastConnectedAt = i.LastConnectedAt,
                    ConversationCount = i.Conversations.Count,
                    UnreadCount = i.Conversations.Sum(c => c.UnreadCount)
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<InstanceDto> CreateAsync(CreateInstanceDto dto, string? createdByUserId, CancellationToken cancellationToken = default)
        {
            var displayName = (dto.DisplayName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                throw new InvalidOperationException("Display name is required.");

            // 1) Duplicate-name check (case-insensitive via SQL Server's default CI collation).
            //    Catches the common case of double-clicks where the first request has already saved.
            var existsByName = await _db.WhatsAppInstances
                .AnyAsync(i => i.DisplayName == displayName, cancellationToken);
            if (existsByName)
                throw new DuplicateInstanceException($"A number named \"{displayName}\" already exists.");

            var instanceName = Slugify(displayName);
            instanceName = await EnsureUniqueInstanceNameAsync(instanceName, cancellationToken);

            var entity = new WhatsAppInstance
            {
                InstanceName = instanceName,
                DisplayName = displayName,
                Status = InstanceStatus.Connecting,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = createdByUserId
            };

            _db.WhatsAppInstances.Add(entity);

            // 2) Race-condition guard. Two parallel requests with the same name can both pass
            //    the AnyAsync check above, but only one can win at SaveChangesAsync because
            //    InstanceName has a unique index. Convert that to a friendly duplicate error.
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _db.WhatsAppInstances.Remove(entity);
                throw new DuplicateInstanceException($"A number named \"{displayName}\" was just created.");
            }

            // Create the instance in Evolution API.
            var client = _httpClientFactory.CreateClient("Evolution");
            var payload = new { instanceName = entity.InstanceName, qrcode = true, integration = "WHATSAPP-BAILEYS" };
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync("/instance/create", body, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Evolution create failed {Status}: {Body}", response.StatusCode, err);
                    entity.Status = InstanceStatus.Disconnected;
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException($"Evolution API returned {(int)response.StatusCode}.");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to reach Evolution API when creating instance {Instance}", entity.InstanceName);
                entity.Status = InstanceStatus.Disconnected;
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }

            await TryRegisterWebhookAsync(entity.InstanceName, cancellationToken);

            return await GetAsync(entity.Id, cancellationToken)
                ?? throw new InvalidOperationException("Instance disappeared after creation.");
        }

        /// <summary>
        /// Auto-registers a webhook on a newly-created Evolution instance so inbound messages flow back here.
        /// Uses Evolution:WebhookPublicBaseUrl from config. If the setting is missing, falls back to copying
        /// the webhook config from any other existing instance (so admins can configure it once via the API).
        /// </summary>
        private async Task TryRegisterWebhookAsync(string instanceName, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("Evolution");

            string? webhookUrl = null;
            string? webhookSecret = _options.WebhookSecret;

            if (!string.IsNullOrWhiteSpace(_options.WebhookPublicBaseUrl))
            {
                webhookUrl = _options.WebhookPublicBaseUrl.TrimEnd('/') + "/api/evolution/webhook";
            }
            else
            {
                // Fallback: copy the webhook URL+secret from another instance (the first one that has one).
                var others = await _db.WhatsAppInstances
                    .Where(i => i.InstanceName != instanceName)
                    .OrderBy(i => i.Id)
                    .Select(i => i.InstanceName)
                    .ToListAsync(cancellationToken);

                foreach (var other in others)
                {
                    try
                    {
                        var resp = await client.GetAsync($"/webhook/find/{other}", cancellationToken);
                        if (!resp.IsSuccessStatusCode) continue;

                        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
                        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "null") continue;

                        using var doc = JsonDocument.Parse(raw);
                        if (doc.RootElement.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                        {
                            webhookUrl = urlEl.GetString();
                        }
                        if (doc.RootElement.TryGetProperty("headers", out var hdrs) &&
                            hdrs.ValueKind == JsonValueKind.Object &&
                            hdrs.TryGetProperty("x-webhook-secret", out var sec) &&
                            sec.ValueKind == JsonValueKind.String)
                        {
                            webhookSecret = sec.GetString() ?? webhookSecret;
                        }

                        if (!string.IsNullOrWhiteSpace(webhookUrl)) break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read webhook from reference instance {Instance}", other);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                _logger.LogWarning("No webhook URL available — inbound messages for {Instance} will not be received until you configure Evolution:WebhookPublicBaseUrl or register a webhook manually.", instanceName);
                return;
            }

            try
            {
                var setPayload = new
                {
                    webhook = new
                    {
                        enabled = true,
                        url = webhookUrl,
                        webhookByEvents = false,
                        webhookBase64 = false,
                        events = new[] { "MESSAGES_UPSERT" },
                        headers = new Dictionary<string, string> { ["x-webhook-secret"] = webhookSecret ?? "" }
                    }
                };

                var body = new StringContent(JsonSerializer.Serialize(setPayload), Encoding.UTF8, "application/json");
                var setResp = await client.PostAsync($"/webhook/set/{instanceName}", body, cancellationToken);

                if (setResp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Registered webhook {Url} for instance {Instance}.", webhookUrl, instanceName);
                }
                else
                {
                    var err = await setResp.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Webhook registration failed for {Instance} ({Status}): {Body}", instanceName, setResp.StatusCode, err);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook registration threw for {Instance}", instanceName);
            }
        }

        public async Task<InstanceQrDto> GetConnectInfoAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _db.WhatsAppInstances.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Instance {id} not found.");

            var client = _httpClientFactory.CreateClient("Evolution");
            var response = await client.GetAsync($"/instance/connect/{entity.InstanceName}", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            string? qr = null;
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("base64", out var b64) && b64.ValueKind == JsonValueKind.String)
                    {
                        qr = b64.GetString();
                    }
                }
                catch (JsonException) { /* instance may already be connected — no QR */ }
            }

            // Also refresh status while we're here.
            await RefreshStatusInternalAsync(entity, cancellationToken);

            return new InstanceQrDto
            {
                Id = entity.Id,
                InstanceName = entity.InstanceName,
                QrBase64 = qr,
                Status = entity.Status
            };
        }

        public async Task<InstanceDto> RefreshStatusAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _db.WhatsAppInstances.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Instance {id} not found.");

            await RefreshStatusInternalAsync(entity, cancellationToken);

            return (await GetAsync(id, cancellationToken))!;
        }

        public async Task DisconnectAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _db.WhatsAppInstances.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Instance {id} not found.");

            var client = _httpClientFactory.CreateClient("Evolution");
            try
            {
                await client.DeleteAsync($"/instance/logout/{entity.InstanceName}", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Logout call failed for {Instance}; marking disconnected anyway.", entity.InstanceName);
            }

            entity.Status = InstanceStatus.Disconnected;
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstancesGlobalGroup)
                .SendAsync("InstanceStatusChanged", new { id = entity.Id, status = (int)entity.Status }, cancellationToken);
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _db.WhatsAppInstances.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new InvalidOperationException($"Instance {id} not found.");

            // Cascade-clean dependent rows (Conversation FK is Restrict, so we delete manually).
            var conversationIds = await _db.Conversations
                .Where(c => c.WhatsAppInstanceId == id)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            if (conversationIds.Count > 0)
            {
                await _db.Messages
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .ExecuteDeleteAsync(cancellationToken);

                await _db.Conversations
                    .Where(c => c.WhatsAppInstanceId == id)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // Try to remove the instance from Evolution API too. If it fails (already gone, network error),
            // we still proceed with local deletion — the user explicitly asked to remove it.
            var client = _httpClientFactory.CreateClient("Evolution");
            try
            {
                await client.DeleteAsync($"/instance/delete/{entity.InstanceName}", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Evolution delete failed for {Instance}; removing locally anyway.", entity.InstanceName);
            }

            _db.WhatsAppInstances.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstancesGlobalGroup)
                .SendAsync("InstanceDeleted", new { id }, cancellationToken);
        }

        // -----------------------------------------------------------------

        private async Task RefreshStatusInternalAsync(WhatsAppInstance entity, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("Evolution");
            try
            {
                var stateResp = await client.GetAsync($"/instance/connectionState/{entity.InstanceName}", cancellationToken);
                if (!stateResp.IsSuccessStatusCode) return;

                var stateJson = await stateResp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(stateJson);
                var state = doc.RootElement.GetProperty("instance").GetProperty("state").GetString();

                var prev = entity.Status;
                entity.Status = state switch
                {
                    "open" => InstanceStatus.Connected,
                    "connecting" => InstanceStatus.Connecting,
                    "close" => InstanceStatus.Disconnected,
                    _ => entity.Status
                };

                if (entity.Status == InstanceStatus.Connected)
                {
                    entity.LastConnectedAt = DateTime.UtcNow;

                    // Fill phone number / ownerJid if we don't have them yet.
                    if (string.IsNullOrWhiteSpace(entity.PhoneNumber) || string.IsNullOrWhiteSpace(entity.OwnerJid))
                    {
                        await PopulateOwnerDetailsAsync(entity, cancellationToken);
                    }
                }

                // Always save: even when Status didn't change, LastConnectedAt or PhoneNumber may have.
                await _db.SaveChangesAsync(cancellationToken);

                if (prev != entity.Status)
                {
                    await _hub.Clients.Group(ChatHub.InstancesGlobalGroup)
                        .SendAsync("InstanceStatusChanged", new { id = entity.Id, status = (int)entity.Status }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh status for {Instance}", entity.InstanceName);
            }
        }

        private async Task PopulateOwnerDetailsAsync(WhatsAppInstance entity, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var resp = await client.GetAsync("/instance/fetchInstances", cancellationToken);
                if (!resp.IsSuccessStatusCode) return;

                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                foreach (var inst in doc.RootElement.EnumerateArray())
                {
                    if (inst.TryGetProperty("name", out var name) && name.GetString() == entity.InstanceName)
                    {
                        if (inst.TryGetProperty("ownerJid", out var jid) && jid.ValueKind == JsonValueKind.String)
                        {
                            entity.OwnerJid = jid.GetString();
                            entity.PhoneNumber = entity.OwnerJid?.Split('@').FirstOrDefault();
                        }
                        break;
                    }
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch owner details for {Instance}", entity.InstanceName);
            }
        }

        private async Task<string> EnsureUniqueInstanceNameAsync(string baseName, CancellationToken cancellationToken)
        {
            var candidate = baseName;
            var suffix = 2;
            while (await _db.WhatsAppInstances.AnyAsync(i => i.InstanceName == candidate, cancellationToken))
            {
                candidate = $"{baseName}-{suffix++}";
            }
            return candidate;
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            // SQL Server unique-index violation is error number 2601 or 2627.
            if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
                return sqlEx.Number is 2601 or 2627;

            var msg = ex.InnerException?.Message ?? ex.Message;
            return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
        }

        private static string Slugify(string input)
        {
            var clean = new string((input ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray());
            while (clean.Contains("--")) clean = clean.Replace("--", "-");
            clean = clean.Trim('-');
            return string.IsNullOrWhiteSpace(clean) ? $"instance-{Guid.NewGuid():N}"[..14] : clean;
        }
    }
}
