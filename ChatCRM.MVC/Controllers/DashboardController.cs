using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IChatService _chatService;
        private readonly IWhatsAppInstanceService _instanceService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            IChatService chatService,
            IWhatsAppInstanceService instanceService,
            ILogger<DashboardController> logger)
        {
            _chatService = chatService;
            _instanceService = instanceService;
            _logger = logger;
        }

        [HttpGet("/dashboard/chats")]
        public async Task<IActionResult> Chats([FromQuery] int? instance, CancellationToken cancellationToken)
        {
            var instances = await _instanceService.GetAllAsync(cancellationToken);

            if (instances.Count == 0)
            {
                return RedirectToAction(nameof(WhatsApp));
            }

            var activeId = instance ?? instances.First().Id;
            if (!instances.Any(i => i.Id == activeId))
                activeId = instances.First().Id;

            var conversations = await _chatService.GetConversationsAsync(activeId, cancellationToken);

            return View(new ChatsPageViewModel
            {
                Instances = instances,
                ActiveInstanceId = activeId,
                Conversations = conversations
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
        public async Task<IActionResult> ConversationsList([FromQuery] int? instance, CancellationToken cancellationToken)
        {
            var items = await _chatService.GetConversationsAsync(instance, cancellationToken);
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
    }
}
