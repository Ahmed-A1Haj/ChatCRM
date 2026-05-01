using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IWhatsAppInstanceService
    {
        Task<List<InstanceDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<InstanceDto?> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<InstanceDto> CreateAsync(CreateInstanceDto dto, string? createdByUserId, CancellationToken cancellationToken = default);
        Task<InstanceQrDto> GetConnectInfoAsync(int id, CancellationToken cancellationToken = default);
        Task<InstanceDto> RefreshStatusAsync(int id, CancellationToken cancellationToken = default);
        Task DisconnectAsync(int id, CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
