using System;
using System.Collections.Generic;
using System.Text;

namespace ChatCRM.Application.Users.DTOS
{
    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
    }
}
