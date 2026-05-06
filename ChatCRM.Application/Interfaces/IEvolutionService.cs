using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IEvolutionService
    {
        Task<EvolutionSendResult> SendMessageAsync(string instanceName, string phone, string message, CancellationToken cancellationToken = default);

        Task<EvolutionSendResult> SendMediaAsync(
            string instanceName,
            string phone,
            string mediaType, // "image" | "video" | "document"
            byte[] data,
            string? mimeType,
            string? fileName,
            string? caption,
            CancellationToken cancellationToken = default);

        Task<EvolutionSendResult> SendVoiceNoteAsync(
            string instanceName,
            string phone,
            byte[] data,
            CancellationToken cancellationToken = default);

        Task<bool> EditMessageAsync(
            string instanceName,
            string remoteJid,
            string externalMessageId,
            string newText,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteMessageAsync(
            string instanceName,
            string remoteJid,
            string externalMessageId,
            CancellationToken cancellationToken = default);

        Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default);
    }

    /// <summary>Outcome of an outbound Evolution API call. ExternalId is the Baileys message ID
    /// returned by the API; we need it later to edit/delete the message.</summary>
    public record EvolutionSendResult(bool Success, string? ExternalId, string? RemoteJid);
}
