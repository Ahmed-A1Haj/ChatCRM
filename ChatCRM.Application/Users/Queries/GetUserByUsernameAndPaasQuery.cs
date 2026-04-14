using ChatCRM.Application.Users.DTOS;
using ChatCRM.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using System.Threading;
using System.Threading.Tasks;

namespace ChatCRM.Application.Users.Queries
{
    public class GetUserByUsernameAndPaasQuery : IRequest<UserDto>
    {
        public LoginDto LO { get; set; } = new LoginDto();
    }

    public class GetUserByUsernameAndPaasQueryHandler : IRequestHandler<GetUserByUsernameAndPaasQuery, UserDto>
    {
        private readonly UserManager<User> _userManager;

        public GetUserByUsernameAndPaasQueryHandler(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        public async Task<UserDto> Handle(GetUserByUsernameAndPaasQuery request, CancellationToken cancellationToken)
        {
            var user = await _userManager.FindByEmailAsync(request.LO.Email);

            if (user == null)
                return null;

            var passwordValid = await _userManager.CheckPasswordAsync(user, request.LO.Password);

            if (!passwordValid)
                return null;

            return new UserDto
            {
                Username = user.UserName,
                Email = user.Email,
            };
        }
    }
}