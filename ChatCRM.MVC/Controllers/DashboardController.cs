using ChatCRM.Application.Billing.Exceptions;
using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Dashboard.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IChatService _chatService;
        private readonly IWhatsAppInstanceService _instanceService;
        private readonly UserManager<User> _userManager;
        private readonly IStringLocalizer _localizer;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IChatService chatService,
            IWhatsAppInstanceService instanceService,
            UserManager<User> userManager,
            IStringLocalizer localizer,
            ILogger<DashboardController> logger)
        {
            _chatService = chatService;
            _instanceService = instanceService;
            _userManager = userManager;
            _localizer = localizer;
            _logger = logger;
        }

        /// <summary>
        /// Post-login landing page. The actual numbers are seeded with realistic mock data right
        /// here — see <c>// TODO</c> markers below for each query that needs to be wired up to
        /// real aggregations once we're ready to take that on.
        /// </summary>
        [HttpGet("/dashboard")]
        public IActionResult Index([FromQuery] string? range)
        {
            var parsed = (range ?? "today").Trim().ToLowerInvariant() switch
            {
                "week"  => DashboardRange.Week,
                "month" => DashboardRange.Month,
                _        => DashboardRange.Today
            };

            var nowUtc = DateTime.UtcNow;

            // ── KPI tiles ────────────────────────────────────────────────────────
            // TODO: replace with Conversations.CountAsync(...) for the selected range + delta math.
            var kpis = new List<KpiTile>
            {
                new() {
                    Key = "OpenConversations",
                    LabelKey = "Dashboard.Kpi.OpenConversations",
                    Value = parsed switch { DashboardRange.Today => "127", DashboardRange.Week => "612", _ => "1,894" },
                    DeltaText = parsed switch { DashboardRange.Today => "+8%", DashboardRange.Week => "+3%", _ => "−1%" },
                    DeltaIsPositive = parsed != DashboardRange.Month,
                    Tone = KpiTone.Neutral
                },
                new() {
                    Key = "Unanswered",
                    LabelKey = "Dashboard.Kpi.Unanswered",
                    Value = parsed switch { DashboardRange.Today => "9", DashboardRange.Week => "21", _ => "47" },
                    DeltaText = parsed switch { DashboardRange.Today => "+2 vs yesterday", DashboardRange.Week => "+4 vs last week", _ => "−5 vs last month" },
                    DeltaIsPositive = parsed == DashboardRange.Month,
                    Tone = parsed == DashboardRange.Month ? KpiTone.Warning : KpiTone.Danger
                },
                new() {
                    Key = "NewContacts",
                    LabelKey = "Dashboard.Kpi.NewContacts",
                    Value = parsed switch { DashboardRange.Today => "14", DashboardRange.Week => "63", _ => "248" },
                    DeltaText = "+12%",
                    DeltaIsPositive = true,
                    Tone = KpiTone.Positive
                },
                new() {
                    Key = "FirstResponseTime",
                    LabelKey = "Dashboard.Kpi.FirstResponseTime",
                    // Target: 5 minutes. Threshold: 10 min becomes Warning, 20+ becomes Danger.
                    // TODO: pull average from Messages where Direction = Incoming followed by Outgoing.
                    Value = parsed switch { DashboardRange.Today => "4 min", DashboardRange.Week => "6 min", _ => "11 min" },
                    DeltaText = parsed switch { DashboardRange.Today => "Within target", DashboardRange.Week => "1 min over target", _ => "6 min over target" },
                    DeltaIsPositive = parsed == DashboardRange.Today,
                    Tone = parsed switch { DashboardRange.Today => KpiTone.Positive, DashboardRange.Week => KpiTone.Warning, _ => KpiTone.Danger }
                }
            };

            // ── Pipeline funnel ──────────────────────────────────────────────────
            // TODO: GROUP BY WhatsAppContact.LifecycleStage WHERE (range filter on conversation activity).
            int[] perStage = parsed switch
            {
                DashboardRange.Today => [42, 18, 11, 7,  9, 4, 3, 5, 2, 1, 8],
                DashboardRange.Week  => [156, 71, 48, 33, 39, 22, 17, 21, 14, 9, 36],
                _                     => [488, 211, 142, 96, 121, 73, 54, 68, 43, 27, 121]
            };
            int[] perStageDelta = [3, -2, 1, 0, 4, -1, 0, 2, 1, 0, 5];
            var maxStage = perStage.Max();
            var pipeline = Enumerable.Range(0, perStage.Length).Select(i => new PipelineStageRow
            {
                Stage = (LifecycleStage)i,
                Count = perStage[i],
                Delta = perStageDelta[i % perStageDelta.Length],
                Percentage = maxStage == 0 ? 0 : Math.Round(perStage[i] * 100.0 / maxStage, 1)
            }).ToList();

            // ── Channel breakdown ───────────────────────────────────────────────
            // TODO: GROUP BY Conversation.Instance.ChannelType WHERE last-message in range.
            var channelTotal = parsed switch { DashboardRange.Today => 127, DashboardRange.Week => 612, _ => 1894 };
            var channels = new List<ChannelSlice>
            {
                new() { Channel = ChannelType.WhatsApp,  Count = (int)(channelTotal * 0.74), Percentage = 74 },
                new() { Channel = ChannelType.Instagram, Count = (int)(channelTotal * 0.18), Percentage = 18 },
                new() { Channel = ChannelType.Messenger, Count = (int)(channelTotal * 0.08), Percentage = 8  }
            };

            // ── Needs-attention queue ───────────────────────────────────────────
            // TODO: SELECT TOP 6 conversations ORDER BY (now - LastIncomingMessageAt) DESC
            // WHERE Status = Open AND (no Outgoing reply since the last incoming).
            var attention = new List<AttentionItem>
            {
                new() { ConversationId = 4218, ContactName = "Layla Mansour",   Initials = "LM", Channel = ChannelType.WhatsApp,  LastMessagePreview = "Hi — still waiting on the quote you mentioned yesterday.",          IdleTime = TimeSpan.FromHours(7) + TimeSpan.FromMinutes(12), Severity = AttentionSeverity.Urgent },
                new() { ConversationId = 4215, ContactName = "Roman D.",        Initials = "RD", Channel = ChannelType.Instagram, LastMessagePreview = "Did you see my reply about the size?",                                IdleTime = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(48), Severity = AttentionSeverity.Urgent },
                new() { ConversationId = 4209, ContactName = "Mehmet Y.",       Initials = "MY", Channel = ChannelType.WhatsApp,  LastMessagePreview = "Sent the photos — let me know if you need anything else.",            IdleTime = TimeSpan.FromHours(4) + TimeSpan.FromMinutes(30), Severity = AttentionSeverity.Urgent },
                new() { ConversationId = 4203, ContactName = "Andrei C.",       Initials = "AC", Channel = ChannelType.Messenger, LastMessagePreview = "Mulțumesc — când pot să trec?",                                       IdleTime = TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5),  Severity = AttentionSeverity.Watching },
                new() { ConversationId = 4198, ContactName = "Yasmin El-Sayed", Initials = "YE", Channel = ChannelType.WhatsApp,  LastMessagePreview = "Sounds good. Let's lock in 10am Tuesday.",                            IdleTime = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(40), Severity = AttentionSeverity.Watching },
                new() { ConversationId = 4192, ContactName = "Carol Singh",     Initials = "CS", Channel = ChannelType.WhatsApp,  LastMessagePreview = "Just sent the invoice via email too.",                                IdleTime = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(11), Severity = AttentionSeverity.Watching }
            };

            // ── Team status ─────────────────────────────────────────────────────
            // TODO: pull from Users + a presence service (last activity from SignalR / cookie).
            var team = new List<TeamMemberRow>
            {
                new() { Name = "Majd Derawi",  Initials = "MD", Presence = PresenceStatus.Online,  ActiveConversationCount = 14, LastActiveUtc = nowUtc.AddMinutes(-1) },
                new() { Name = "Ahmed Sayed",  Initials = "AS", Presence = PresenceStatus.Online,  ActiveConversationCount = 9,  LastActiveUtc = nowUtc.AddMinutes(-3) },
                new() { Name = "Nadia Rahimi", Initials = "NR", Presence = PresenceStatus.Away,    ActiveConversationCount = 6,  LastActiveUtc = nowUtc.AddMinutes(-22) },
                new() { Name = "Tomek K.",     Initials = "TK", Presence = PresenceStatus.Away,    ActiveConversationCount = 4,  LastActiveUtc = nowUtc.AddMinutes(-48) },
                new() { Name = "Sofia Petrov", Initials = "SP", Presence = PresenceStatus.Offline, ActiveConversationCount = 2,  LastActiveUtc = nowUtc.AddHours(-3) }
            };

            // ── Recent activity feed ────────────────────────────────────────────
            // TODO: stream from a server-side activity log (SignalR push when wired live).
            var activity = new List<ActivityEntry>
            {
                new() { Type = ActivityType.Assigned,         AtUtc = nowUtc.AddMinutes(-2),  DescriptionHtml = "<strong>Ahmed Sayed</strong> was assigned to <strong>Layla Mansour</strong>." },
                new() { Type = ActivityType.MessageSent,      AtUtc = nowUtc.AddMinutes(-5),  DescriptionHtml = "<strong>Majd Derawi</strong> replied to <strong>Carol Singh</strong>." },
                new() { Type = ActivityType.LifecycleChanged, AtUtc = nowUtc.AddMinutes(-9),  DescriptionHtml = "<strong>Roman D.</strong> moved to <em>Wants a Meeting</em>." },
                new() { Type = ActivityType.StatusChanged,    AtUtc = nowUtc.AddMinutes(-14), DescriptionHtml = "<strong>Yasmin El-Sayed</strong>'s conversation was marked <em>Closed</em>." },
                new() { Type = ActivityType.NewContact,       AtUtc = nowUtc.AddMinutes(-17), DescriptionHtml = "New contact <strong>Tobias Lange</strong> via WhatsApp." },
                new() { Type = ActivityType.NoteAdded,        AtUtc = nowUtc.AddMinutes(-24), DescriptionHtml = "<strong>Nadia Rahimi</strong> added a note on <strong>Mehmet Y.</strong>." },
                new() { Type = ActivityType.Assigned,         AtUtc = nowUtc.AddMinutes(-31), DescriptionHtml = "<strong>Sofia Petrov</strong> was assigned to <strong>Andrei C.</strong>." },
                new() { Type = ActivityType.MessageSent,      AtUtc = nowUtc.AddMinutes(-38), DescriptionHtml = "<strong>Tomek K.</strong> replied to <strong>Mehmet Y.</strong>." },
                new() { Type = ActivityType.LifecycleChanged, AtUtc = nowUtc.AddHours(-1),    DescriptionHtml = "<strong>Carol Singh</strong> moved to <em>Will Make Payment</em>." },
                new() { Type = ActivityType.StatusChanged,    AtUtc = nowUtc.AddHours(-2),    DescriptionHtml = "<strong>Roman D.</strong>'s conversation was reopened." }
            };

            var vm = new DashboardViewModel
            {
                Range = parsed,
                LastUpdatedUtc = nowUtc,
                TimeZoneAbbreviation = TimeZoneInfo.Local.DisplayName, // TODO: per-user timezone preference
                Kpis = kpis,
                Pipeline = pipeline,
                Channels = channels,
                Attention = attention,
                Team = team,
                Activity = activity
            };

            return View(vm);
        }

        [HttpGet("/dashboard/chats")]
        public async Task<IActionResult> Chats(
            [FromQuery] int? instance,
            [FromQuery] string? filter,
            [FromQuery] byte? channel,
            CancellationToken cancellationToken)
        {
            var instances = await _instanceService.GetAllAsync(cancellationToken);

            if (instances.Count == 0)
                return RedirectToAction(nameof(WhatsApp));

            var activeId = instance ?? instances.First().Id;
            if (!instances.Any(i => i.Id == activeId))
                activeId = instances.First().Id;

            var userId = _userManager.GetUserId(User);
            var normalizedFilter = (filter ?? "all").ToLowerInvariant();

            var conversations   = await _chatService.GetConversationsAsync(activeId, normalizedFilter, userId, channel, cancellationToken);
            var team            = await _chatService.GetTeamMembersAsync(cancellationToken);
            var allItems        = await _chatService.GetConversationsAsync(activeId, "all", userId, channel, cancellationToken);
            var mineItems       = await _chatService.GetConversationsAsync(activeId, "mine", userId, channel, cancellationToken);
            var unassignedItems = await _chatService.GetConversationsAsync(activeId, "unassigned", userId, channel, cancellationToken);

            return View(new ChatsPageViewModel
            {
                Instances = instances,
                ActiveInstanceId = activeId,
                Conversations = conversations,
                Filter = normalizedFilter,
                ActiveChannelType = channel,
                TeamMembers = team,
                CurrentUserId = userId,
                CountAll = allItems.Count,
                CountMine = mineItems.Count,
                CountUnassigned = unassignedItems.Count
            });
        }

        [HttpGet("/dashboard/whatsapp")]
        public async Task<IActionResult> WhatsApp(CancellationToken cancellationToken)
        {
            var instances = await _instanceService.GetAllAsync(cancellationToken);
            return View(instances);
        }

        [HttpGet("/dashboard/chats/{id:int}/messages")]
        public async Task<IActionResult> Messages(int id, CancellationToken cancellationToken)
        {
            var messages = await _chatService.GetMessagesAsync(id, cancellationToken);
            await _chatService.MarkAsReadAsync(id, cancellationToken);
            return Json(messages);
        }

        [HttpGet("/dashboard/chats/list")]
        public async Task<IActionResult> ConversationsList([FromQuery] int? instance, [FromQuery] string? filter, [FromQuery] byte? channel, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            var items = await _chatService.GetConversationsAsync(instance, filter, userId, channel, cancellationToken);
            return Json(items);
        }

        [HttpPost("/dashboard/chats/send")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send([FromBody] SendMessageDto dto, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var message = await _chatService.SendMessageAsync(dto, userId, cancellationToken);
                return Json(message);
            }
            catch (InsufficientBalanceException ex)
            {
                _logger.LogInformation("Send blocked — insufficient balance (have {Have:0.0000}, need {Need:0.0000}) for conversation {Id}",
                    ex.CurrentBalanceUsd, ex.RequiredUsd, dto.ConversationId);
                return StatusCode(402, new { error = _localizer["Send.Error.InsufficientBalance"].Value, code = "insufficient_balance" });
            }
            catch (ServiceWindowExpiredException ex)
            {
                _logger.LogInformation("Send blocked — service window closed (last incoming {LastIn}) for conversation {Id}",
                    ex.LastIncomingAtUtc, dto.ConversationId);
                return StatusCode(409, new { error = _localizer["Send.Error.ServiceWindowExpired"].Value, code = "service_window_expired" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Send failed for conversation {Id}", dto.ConversationId);
                return NotFound(new { error = ex.Message });
            }
        }

        public sealed class SendTemplateRequest
        {
            public int ConversationId { get; set; }
            public int TemplateId { get; set; }
            public List<string> Variables { get; set; } = new();
        }

        [HttpPost("/dashboard/chats/send-template")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTemplate([FromBody] SendTemplateRequest dto, CancellationToken cancellationToken)
        {
            if (dto is null) return BadRequest(new { error = "Body required." });

            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var message = await _chatService.SendTemplateMessageAsync(
                    dto.ConversationId, dto.TemplateId, dto.Variables, userId, cancellationToken);
                return Json(message);
            }
            catch (InsufficientBalanceException)
            {
                return StatusCode(402, new { error = _localizer["Send.Error.InsufficientBalance"].Value, code = "insufficient_balance" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "SendTemplate failed for conversation {Id}", dto.ConversationId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/chats/note")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNote([FromBody] AddNoteDto dto, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dto.Body))
                return BadRequest(new { error = "Note body required." });

            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var note = await _chatService.AddNoteAsync(dto, userId, cancellationToken);
                return Json(note);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/chats/assign")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign([FromBody] AssignDto dto, CancellationToken cancellationToken)
        {
            try
            {
                await _chatService.AssignAsync(dto, cancellationToken);
                return Ok(new { assigned = true });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/chats/status")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus([FromBody] SetStatusDto dto, CancellationToken cancellationToken)
        {
            try
            {
                await _chatService.SetStatusAsync(dto, cancellationToken);
                return Ok(new { status = dto.Status });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        public class EditMessageRequest
        {
            public int MessageId { get; set; }
            public string Body { get; set; } = string.Empty;
        }

        [HttpPost("/dashboard/chats/edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMessage([FromBody] EditMessageRequest dto, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dto.Body))
                return BadRequest(new { error = "Edited message body is required." });

            try
            {
                var message = await _chatService.EditMessageAsync(dto.MessageId, dto.Body, cancellationToken);
                return Json(message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Edit failed for message {Id}", dto.MessageId);
                return BadRequest(new { error = ex.Message });
            }
        }

        public class DeleteMessageRequest { public int MessageId { get; set; } }

        [HttpPost("/dashboard/chats/delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMessage([FromBody] DeleteMessageRequest dto, CancellationToken cancellationToken)
        {
            try
            {
                await _chatService.DeleteMessageAsync(dto.MessageId, cancellationToken);
                return Ok(new { deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Delete failed for message {Id}", dto.MessageId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/chats/send-media")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(30_000_000)] // 30 MB upload cap
        public async Task<IActionResult> SendMedia(
            [FromForm] int conversationId,
            [FromForm] IFormFile file,
            [FromForm] string? caption,
            CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "File is required." });

            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, cancellationToken);
                var bytes = ms.ToArray();

                var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
                var message = await _chatService.SendMediaMessageAsync(conversationId, bytes, file.FileName, mime, caption, userId, cancellationToken);
                return Json(message);
            }
            catch (InsufficientBalanceException)
            {
                return StatusCode(402, new { error = _localizer["Send.Error.InsufficientBalance"].Value, code = "insufficient_balance" });
            }
            catch (ServiceWindowExpiredException)
            {
                return StatusCode(409, new { error = _localizer["Send.Error.ServiceWindowExpired"].Value, code = "service_window_expired" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "SendMedia failed for conversation {Id}", conversationId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/chats/send-voice")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(15_000_000)]
        public async Task<IActionResult> SendVoice(
            [FromForm] int conversationId,
            [FromForm] IFormFile audio,
            CancellationToken cancellationToken)
        {
            if (audio is null || audio.Length == 0)
                return BadRequest(new { error = "Audio file is required." });

            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                using var ms = new MemoryStream();
                await audio.CopyToAsync(ms, cancellationToken);
                var bytes = ms.ToArray();

                var mime = string.IsNullOrWhiteSpace(audio.ContentType) ? "audio/ogg" : audio.ContentType;
                var message = await _chatService.SendVoiceNoteAsync(conversationId, bytes, mime, userId, cancellationToken);
                return Json(message);
            }
            catch (InsufficientBalanceException)
            {
                return StatusCode(402, new { error = _localizer["Send.Error.InsufficientBalance"].Value, code = "insufficient_balance" });
            }
            catch (ServiceWindowExpiredException)
            {
                return StatusCode(409, new { error = _localizer["Send.Error.ServiceWindowExpired"].Value, code = "service_window_expired" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "SendVoice failed for conversation {Id}", conversationId);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/chats/lifecycle")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetLifecycle([FromBody] SetLifecycleDto dto, CancellationToken cancellationToken)
        {
            try
            {
                await _chatService.SetLifecycleStageAsync(dto, cancellationToken);
                return Ok(new { stage = dto.Stage });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("/dashboard/chats/{id:int}/contact")]
        public async Task<IActionResult> ContactDetails(int id, CancellationToken cancellationToken)
        {
            var details = await _chatService.GetContactDetailsAsync(id, cancellationToken);
            return details is null ? NotFound() : Json(details);
        }

        // ── Audio transcription (inspection) ────────────────────────────
        [HttpGet("/dashboard/chats/messages/{messageId:int}/transcription")]
        public async Task<IActionResult> Transcription(int messageId, CancellationToken cancellationToken)
        {
            var dto = await _chatService.GetTranscriptionAsync(messageId, cancellationToken);
            return dto is null ? NotFound() : Json(dto);
        }

        [HttpPost("/dashboard/chats/messages/{messageId:int}/transcribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryTranscription(int messageId, CancellationToken cancellationToken)
        {
            var ok = await _chatService.RetryTranscriptionAsync(messageId, cancellationToken);
            return ok ? Ok(new { queued = true }) : BadRequest(new { error = "Message is not a transcribable audio message." });
        }

        /// <summary>
        /// Shared Media &amp; Files gallery for one conversation — aggregates every image, file and
        /// link exchanged in the thread. Renders the page shell; the grid/list is filled by
        /// <see cref="Attachments"/> over JSON.
        /// </summary>
        [HttpGet("/dashboard/chats/{id:int}/media")]
        public async Task<IActionResult> Media(int id, CancellationToken cancellationToken)
        {
            var vm = await _chatService.GetMediaGalleryAsync(id, cancellationToken);
            return vm is null ? NotFound() : View(vm);
        }

        [HttpGet("/dashboard/chats/{id:int}/attachments")]
        public async Task<IActionResult> Attachments(int id, [FromQuery] AttachmentsQuery query, CancellationToken cancellationToken)
        {
            var result = await _chatService.GetAttachmentsAsync(id, query, cancellationToken);
            return Json(result);
        }
    }
}
