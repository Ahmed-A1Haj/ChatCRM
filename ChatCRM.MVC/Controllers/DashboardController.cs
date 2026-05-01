using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IChatService _chatService;
        private readonly IWhatsAppInstanceService _instanceService;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IChatService chatService,
            IWhatsAppInstanceService instanceService,
            UserManager<User> userManager,
            ILogger<DashboardController> logger)
        {
            _chatService = chatService;
            _instanceService = instanceService;
            _userManager = userManager;
            _logger = logger;
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

            try
            {
                var message = await _chatService.SendMessageAsync(dto, cancellationToken);
                return Json(message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Send failed for conversation {Id}", dto.ConversationId);
                return NotFound(new { error = ex.Message });
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
    }
}
