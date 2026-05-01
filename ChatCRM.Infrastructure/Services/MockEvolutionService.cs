using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// Dev-only replacement for EvolutionService. Swallows outbound sends, no webhook traffic.
    /// Activated when Evolution:UseMock=true in appsettings.
    /// </summary>
    public class MockEvolutionService : IEvolutionService
    {
        private readonly ILogger<MockEvolutionService> _logger;

        public MockEvolutionService(ILogger<MockEvolutionService> logger)
        {
            _logger = logger;
        }

        public Task<bool> SendMessageAsync(string instanceName, string phone, string message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK] Would send via {Instance} to {Phone}: {Message}", instanceName, phone, message);
            return Task.FromResult(true);
        }

        public Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
