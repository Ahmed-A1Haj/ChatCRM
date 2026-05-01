using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Users.DTOS;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Services;
using ChatCRM.MVC.Services;
using ChatCRM.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

DotEnvLoader.Load(
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

builder.Services.AddControllersWithViews();
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

// Skip HTTPS redirect for webhook so external senders (Evolution API) can POST over HTTP.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/evolution"),
    branch => branch.UseHttpsRedirection());

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ChatHub>("/hubs/chat");

app.Run();
