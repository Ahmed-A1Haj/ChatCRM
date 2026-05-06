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

        public Task<EvolutionSendResult> SendMessageAsync(string instanceName, string phone, string message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK] Would send via {Instance} to {Phone}: {Message}", instanceName, phone, message);
            return Task.FromResult(new EvolutionSendResult(true, $"mock-{Guid.NewGuid():N}", $"{phone}@s.whatsapp.net"));
        }

        public Task<EvolutionSendResult> SendMediaAsync(string instanceName, string phone, string mediaType, byte[] data, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK] Would send {Type} ({Bytes} bytes) via {Instance} to {Phone}", mediaType, data.Length, instanceName, phone);
            return Task.FromResult(new EvolutionSendResult(true, $"mock-{Guid.NewGuid():N}", $"{phone}@s.whatsapp.net"));
        }

        public Task<EvolutionSendResult> SendVoiceNoteAsync(string instanceName, string phone, byte[] data, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK] Would send voice ({Bytes} bytes) via {Instance} to {Phone}", data.Length, instanceName, phone);
            return Task.FromResult(new EvolutionSendResult(true, $"mock-{Guid.NewGuid():N}", $"{phone}@s.whatsapp.net"));
        }

        public Task<bool> EditMessageAsync(string instanceName, string remoteJid, string externalMessageId, string newText, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK] Would edit {Id} via {Instance} to: {Text}", externalMessageId, instanceName, newText);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteMessageAsync(string instanceName, string remoteJid, string externalMessageId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK] Would delete {Id} via {Instance}", externalMessageId, instanceName);
            return Task.FromResult(true);
        }

        public Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
