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
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(IChatService chatService, ILogger<DashboardController> logger)
        {
            _chatService = chatService;
            _logger = logger;
        }

        [HttpGet("/dashboard/chats")]
        public async Task<IActionResult> Chats(CancellationToken cancellationToken)
        {
            var conversations = await _chatService.GetConversationsAsync(cancellationToken);
            return View(conversations);
        }

        [HttpGet("/dashboard/chats/{id:int}/messages")]
        public async Task<IActionResult> Messages(int id, CancellationToken cancellationToken)
        {
            var messages = await _chatService.GetMessagesAsync(id, cancellationToken);
            await _chatService.MarkAsReadAsync(id, cancellationToken);
            return Json(messages);
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
