namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Canonical list of permission keys used for RBAC.
    /// Permissions are stored as RoleClaims of type "Permission" with one of these values.
    /// </summary>
    public static class Permissions
    {
        public const string ClaimType = "Permission";

        // ── User & role administration ────────────────────────────────
        public const string UsersView   = "users.view";
        public const string UsersManage = "users.manage";
        public const string RolesManage = "roles.manage";

        // ── Contacts ───────────────────────────────────────────────────
        public const string ContactsView   = "contacts.view";
        public const string ContactsEdit   = "contacts.edit";
        public const string ContactsDelete = "contacts.delete";

        // ── Conversations ──────────────────────────────────────────────
        public const string ConversationsAssign = "conversations.assign";
        public const string ConversationsClose  = "conversations.close";

        // ── Channels (instances) ──────────────────────────────────────
        public const string ChannelsManage = "channels.manage";

        // ── Settings ───────────────────────────────────────────────────
        public const string SettingsView = "settings.view";

        /// <summary>All permissions, used for seeding the Admin role.</summary>
        public static readonly string[] All =
        {
            UsersView, UsersManage, RolesManage,
            ContactsView, ContactsEdit, ContactsDelete,
            ConversationsAssign, ConversationsClose,
            ChannelsManage,
            SettingsView
        };

        /// <summary>Logical groupings used by the role-editor UI to render checkboxes.</summary>
        public static readonly Dictionary<string, string[]> Groups = new()
        {
            ["Users & roles"]  = new[] { UsersView, UsersManage, RolesManage },
            ["Contacts"]       = new[] { ContactsView, ContactsEdit, ContactsDelete },
            ["Conversations"]  = new[] { ConversationsAssign, ConversationsClose },
            ["Channels"]       = new[] { ChannelsManage },
            ["Settings"]       = new[] { SettingsView }
        };

        public static readonly Dictionary<string, string> Labels = new()
        {
            [UsersView]            = "View users",
            [UsersManage]          = "Manage users (create, edit, delete)",
            [RolesManage]          = "Manage roles & permissions",
            [ContactsView]         = "View contacts",
            [ContactsEdit]         = "Edit contacts",
            [ContactsDelete]       = "Delete contacts",
            [ConversationsAssign]  = "Assign conversations",
            [ConversationsClose]   = "Close / reopen conversations",
            [ChannelsManage]       = "Manage channels (WhatsApp numbers)",
            [SettingsView]         = "Access settings"
        };
    }

    public static class Roles
    {
        public const string Admin   = "Admin";
        public const string Manager = "Manager";
        public const string Agent   = "Agent";

        public static readonly string[] All = { Admin, Manager, Agent };

        /// <summary>Lucide icon name + accent color class for at-a-glance visual recognition per role.</summary>
        public static readonly Dictionary<string, (string icon, string accent)> Visuals = new()
        {
            [Admin]   = ("shield",    "role-accent-red"),     // top authority
            [Manager] = ("briefcase", "role-accent-amber"),   // operations lead
            [Agent]   = ("headset",   "role-accent-indigo")   // front-line responder
        };

        public const string DefaultIcon   = "lock";
        public const string DefaultAccent = "role-accent-slate";
    }
}
