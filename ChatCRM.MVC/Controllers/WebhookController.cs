using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChatCRM.MVC.Controllers
{
    [ApiController]
    [Route("api/evolution")]
    public class WebhookController : ControllerBase
    {
        private readonly IEvolutionService _evolutionService;
        private readonly EvolutionOptions _options;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IEvolutionService evolutionService,
            IOptions<EvolutionOptions> options,
            ILogger<WebhookController> logger)
        {
            _evolutionService = evolutionService;
            _options = options.Value;
            _logger = logger;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(
            [FromBody] WebhookPayloadDto payload,
            [FromHeader(Name = "x-webhook-secret")] string? secret,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_options.WebhookSecret) &&
                !string.Equals(secret, _options.WebhookSecret, StringComparison.Ordinal))
            {
                _logger.LogWarning("Webhook rejected — invalid secret.");
                return Unauthorized();
            }

            _logger.LogInformation("Webhook received: event={Event}", payload.Event);

            if (payload.Event is "messages.upsert" or "messages.received")
            {
                await _evolutionService.HandleIncomingWebhookAsync(payload, cancellationToken);
            }

            return Ok();
        }
    }
}
