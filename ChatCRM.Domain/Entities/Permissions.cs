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

        // ── Billing & wallet ───────────────────────────────────────────
        public const string BillingView         = "billing.view";          // see balance, transactions, invoices
        public const string BillingTopUp        = "billing.topup";         // initiate top-ups (Manager+)
        public const string BillingAdminRefund  = "billing.admin.refund";  // issue manual refunds (Admin only)

        // ── WhatsApp templates ─────────────────────────────────────────
        public const string TemplatesView   = "templates.view";
        public const string TemplatesCreate = "templates.create";
        public const string TemplatesSubmit = "templates.submit";          // send to Meta for approval
        public const string TemplatesDelete = "templates.delete";

        // ── Platform-level admin (cross-workspace, controlled by config "Platform:Admins") ──
        // This claim is only granted to a synthetic "Platform Admin" role seeded for accounts
        // listed in appsettings under Platform:Admins. It gates /admin/* routes.
        public const string PlatformAdmin = "platform.admin";

        /// <summary>All permissions, used for seeding the Admin role.</summary>
        public static readonly string[] All =
        {
            UsersView, UsersManage, RolesManage,
            ContactsView, ContactsEdit, ContactsDelete,
            ConversationsAssign, ConversationsClose,
            ChannelsManage,
            SettingsView,
            BillingView, BillingTopUp, BillingAdminRefund,
            TemplatesView, TemplatesCreate, TemplatesSubmit, TemplatesDelete
            // Note: PlatformAdmin is intentionally NOT in `All` — even workspace Admins
            // shouldn't get it by default. It's granted only via the Platform:Admins config list.
        };

        /// <summary>Logical groupings used by the role-editor UI to render checkboxes.</summary>
        public static readonly Dictionary<string, string[]> Groups = new()
        {
            ["Users & roles"]  = new[] { UsersView, UsersManage, RolesManage },
            ["Contacts"]       = new[] { ContactsView, ContactsEdit, ContactsDelete },
            ["Conversations"]  = new[] { ConversationsAssign, ConversationsClose },
            ["Channels"]       = new[] { ChannelsManage },
            ["Settings"]       = new[] { SettingsView },
            ["Billing"]        = new[] { BillingView, BillingTopUp, BillingAdminRefund },
            ["Templates"]      = new[] { TemplatesView, TemplatesCreate, TemplatesSubmit, TemplatesDelete }
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
            [SettingsView]         = "Access settings",
            [BillingView]          = "View billing balance, transactions, invoices",
            [BillingTopUp]         = "Top up the wallet",
            [BillingAdminRefund]   = "Issue manual refunds & balance adjustments",
            [TemplatesView]        = "View WhatsApp templates",
            [TemplatesCreate]      = "Create & edit templates",
            [TemplatesSubmit]      = "Submit templates to Meta for approval",
            [TemplatesDelete]      = "Delete templates",
            [PlatformAdmin]        = "Platform admin (cross-workspace)"
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
