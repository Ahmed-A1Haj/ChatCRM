using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Users.DTOS;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Services;
using ChatCRM.Infrastructure.Services.Ai;
using ChatCRM.MVC.Localization;
using ChatCRM.MVC.Services;
using ChatCRM.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using StackExchange.Redis;
using System.Globalization;

DotEnvLoader.Load(
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    // Hand-rolled migrations occasionally drift from EF's auto-generated snapshot in cosmetic
    // ways (column order, annotation format) — the database is correct, but EF Core 10 promotes
    // that drift to a startup-blocking error by default. Downgrade it so the app boots; we'll
    // resync the snapshot the next time the dev server isn't holding pdb locks.
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});

builder.Services
    .AddIdentity<User, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 4;

        options.SignIn.RequireConfirmedEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
});

// ── Localization (custom JSON-backed IStringLocalizer) ────────────
builder.Services.AddSingleton<JsonStringLocalizer.ResourceCache>();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddSingleton<IStringLocalizer>(sp =>
    sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(object)));

var supportedCultures = new[] { "en", "ru", "ro", "tr" }
    .Select(c => new CultureInfo(c))
    .ToList();

builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    opts.DefaultRequestCulture = new RequestCulture("en");
    opts.SupportedCultures = supportedCultures;
    opts.SupportedUICultures = supportedCultures;
    // Provider order: ?culture=xx → cookie → Accept-Language header.
    // Auto-detection from the browser is the default fallback before we hit "en".
    opts.RequestCultureProviders =
    [
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});

builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();

// SignalR
builder.Services.AddSignalR();

// Evolution API
builder.Services.Configure<EvolutionOptions>(builder.Configuration.GetSection("Evolution"));
builder.Services.AddHttpClient("Evolution", (sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EvolutionOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.DefaultRequestHeaders.Add("apikey", opts.ApiKey);
});

// Chat services
var useMockEvolution = builder.Configuration.GetValue<bool>("Evolution:UseMock");
if (useMockEvolution)
{
    builder.Services.AddScoped<IEvolutionService, MockEvolutionService>();
    builder.Services.AddHostedService<FakeMessageSimulator>();
}
else
{
    builder.Services.AddScoped<IEvolutionService, EvolutionService>();
}
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IWhatsAppInstanceService, WhatsAppInstanceService>();
builder.Services.AddScoped<IContactsService, ContactsService>();
builder.Services.AddScoped<IContactImportService, ContactImportService>();

// Billing — pricing engine (phase 1) + wallet read/admin surface (phase 2) + Stripe top-up
// flow (phase 3) + per-message billing gate (phase 4: reserve → send → commit/release).
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IBillingGate, ChatCRM.Infrastructure.Services.Billing.BillingGate>();

// Templates (phase 6) — direct Meta Graph API integration. The named HttpClient lets us
// add Polly / DefaultRequestHeaders later without touching the provider.
builder.Services.Configure<ChatCRM.Infrastructure.Services.Templates.MetaGraphOptions>(
    builder.Configuration.GetSection("Meta:Graph"));
builder.Services.AddHttpClient(ChatCRM.Infrastructure.Services.Templates.MetaGraphTemplateProvider.HttpClientName);
builder.Services.AddScoped<IWhatsAppTemplateProvider, ChatCRM.Infrastructure.Services.Templates.MetaGraphTemplateProvider>();
// Singleton: in-process tracker for the last Meta auth failure so the UI preflight can
// surface "rotate the token" without making every request hit Meta first.
builder.Services.AddSingleton<ITemplateProviderHealth, ChatCRM.Infrastructure.Services.Templates.TemplateProviderHealth>();
builder.Services.AddScoped<ITemplateService, ChatCRM.Infrastructure.Services.Templates.TemplateService>();
// Adaptive-cadence poller that keeps Submitted templates in sync with Meta. Self-paced
// (≤30m: 2m / ≤6h: 10m / ≤24h: 30m / >24h: 2h, gives up at 7d).
builder.Services.AddHostedService<ChatCRM.Infrastructure.Services.Templates.TemplateStatusSyncService>();

// Invoicing (phase 9) — monthly statements rendered via QuestPDF on demand.
builder.Services.AddScoped<ChatCRM.Infrastructure.Services.Invoices.IInvoicePdfRenderer,
    ChatCRM.Infrastructure.Services.Invoices.QuestPdfInvoiceRenderer>();
builder.Services.AddScoped<IInvoiceService, ChatCRM.Infrastructure.Services.Invoices.InvoiceService>();

// Platform admin (phase 10) — cross-workspace finance reporting.
builder.Services.AddScoped<IPlatformAdminService, ChatCRM.Infrastructure.Services.Admin.PlatformAdminService>();

// Auto-recharge worker (phase 11) — ticks every 5 min; charges saved card off-session
// when the wallet falls below its trigger threshold. Self-disables on charge failure.
builder.Services.AddHostedService<ChatCRM.Infrastructure.Services.Payments.AutoRechargeWorker>();

// Audit log (phase 12) — read-only paged query over BillingAuditLog.
builder.Services.AddScoped<IAuditLogService, ChatCRM.Infrastructure.Services.Audit.AuditLogService>();

// AI Agents (phase 13) — workspace-scoped agents + per-conversation assignment.
builder.Services.AddScoped<IAgentService, ChatCRM.Infrastructure.Services.Agents.AgentService>();

// AI integration (phase 14) — outbox-backed bridge to CRM-AI-Service over Redis Streams.
// Producer side: IAiAgentClient writes to AiOutboxMessages; AiOutboxPublisher drains to
// stream:inbound via XADD. Consumer side: AiOutboundConsumer XREADGROUP from
// stream:outbound, dispatches to AiReplyDispatcher which reuses the existing
// BillingGate + Evolution + SignalR pipeline.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
    // AI/Redis is optional (see README). Disable abort-on-connect-fail so a missing Redis
    // doesn't crash startup — the multiplexer connects in the background and the AI workers
    // retry once Redis becomes reachable.
    var redisConfig = ConfigurationOptions.Parse(opts.RedisUrl);
    redisConfig.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(redisConfig);
});
builder.Services.AddScoped<IAiAgentClient, AiAgentClient>();
builder.Services.AddScoped<IAiReplyDispatcher, AiReplyDispatcher>();
builder.Services.AddHostedService<AiOutboxPublisher>();
builder.Services.AddHostedService<AiOutboundConsumer>();

// Stripe configuration — keys live in environment / appsettings.Development.json.
// The provider self-checks IsConfigured before any Stripe API call so missing keys
// surface as a friendly "Payment processing is not configured" message rather than a 500.
builder.Services.Configure<ChatCRM.Infrastructure.Services.Payments.StripeOptions>(
    builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<IPaymentProvider, ChatCRM.Infrastructure.Services.Payments.StripePaymentProvider>();
builder.Services.AddScoped<IBillingEmailSender, ChatCRM.MVC.Services.BillingEmailSender>();
builder.Services.AddMemoryCache(); // for top-up rate limiting

builder.Services.Configure<SmtpEmailOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddValidatorsFromAssemblyContaining<LoginDtoValidator>();
builder.Services.AddScoped<IEmailSender<User>, SmtpEmailSender>();
builder.Services.AddScoped<IProfileImageStorageService, ProfileImageStorageService>();

// Permission-based authorization
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    ChatCRM.Infrastructure.Authorization.PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");

    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");

        await InstanceSeeder.SeedDefaultIfEmptyAsync(
            dbContext,
            builder.Configuration["Evolution:InstanceName"],
            logger);

        if (useMockEvolution)
        {
            await DemoDataSeeder.SeedAsync(dbContext, logger);
        }

        // RBAC seeding — roles + permission claims + first-user→Admin promotion.
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        await RoleSeeder.SeedAsync(roleManager, userManager, logger);

        // Billing — ensure the singleton workspace wallet, billing settings, and Meta pricing
        // rules exist. Idempotent on subsequent boots; the JSON file is read on first run only,
        // after which the DB is the source of truth (admin UI edits in phase 10).
        var pricingJsonPath = Path.Combine(builder.Environment.ContentRootPath, "Resources", "billing", "meta-pricing.json");
        await BillingSeeder.SeedAsync(dbContext, logger, pricingJsonPath);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations.");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Skip HTTPS redirect for webhook senders that POST over plain HTTP — Evolution API
// (WhatsApp messages) and Stripe (when proxied via ngrok in dev).
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/evolution")
        && !ctx.Request.Path.StartsWithSegments("/api/webhooks/stripe"),
    branch => branch.UseHttpsRedirection());

app.UseStaticFiles();

// Apply request culture (query > cookie > Accept-Language) to every request.
// Setting CurrentCulture also drives DateTime/number/currency formatting throughout the request.
app.UseRequestLocalization();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ChatHub>("/hubs/chat");

app.Run();
