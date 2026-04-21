using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IEvolutionService
    {
        Task<bool> SendMessageAsync(string phone, string message, CancellationToken cancellationToken = default);
        Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default);
    }
}
