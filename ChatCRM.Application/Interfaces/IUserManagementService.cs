using ChatCRM.Application.Users.DTOS;

namespace ChatCRM.Application.Interfaces
{
    public interface IUserManagementService
    {
        Task<List<UserListItemDto>> ListAsync(string? search = null, CancellationToken cancellationToken = default);
        Task<UserListItemDto?> GetAsync(string id, CancellationToken cancellationToken = default);
        Task<UserListItemDto> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);
        Task<UserListItemDto> UpdateAsync(UpdateUserDto dto, CancellationToken cancellationToken = default);
        Task SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken = default);
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    }

    public interface IRoleManagementService
    {
        Task<List<RoleListItemDto>> ListAsync(CancellationToken cancellationToken = default);
        Task<RoleListItemDto?> GetAsync(string id, CancellationToken cancellationToken = default);
        Task<RoleListItemDto> SaveAsync(SaveRoleDto dto, CancellationToken cancellationToken = default);
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    }
}
