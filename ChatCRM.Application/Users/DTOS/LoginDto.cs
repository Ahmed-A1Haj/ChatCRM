using System;
using System.Collections.Generic;
using System.Text;

namespace ChatCRM.Application.Users.DTOS
{
    public class LoginDto
    {
        public string UserName { get; set; } = null!;

        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;

        public bool RememberMe { get; set; }



    }
}
