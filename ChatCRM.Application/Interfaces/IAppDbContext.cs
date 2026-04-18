using ChatCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatCRM.Application.Interfaces
{
    public interface IAppDbContext
    {
        DbSet<User> Users { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    }
}
