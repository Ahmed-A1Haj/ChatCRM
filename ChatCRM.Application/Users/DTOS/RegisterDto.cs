using System;
using System.Collections.Generic;
using System.Text;

namespace ChatCRM.Application.Users.DTOS
{
    public class RegisterDto
    {

        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string ConfirmPassword { get; set; } = null!;
    }
}
