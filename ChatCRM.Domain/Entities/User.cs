using ChatCRM.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace ChatCRM.Domain.Entities
{
    public class User : BaseEntityId
    {
        public string UserName { get; set; } = null!;
        public string Password { get; set; } = null!;
        public int RoleId { get; set; }
    }
}
