using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu;
using RestaurantMenu.Models;
using System.Globalization;
using RestaurantMenu.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using RestaurantMenu.Filters;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Uploaded images and data-protection keys live on Render's persistent disk.
// Locally there is no /var/data (and it needs root), so default to ~/.myquickmenu. Not inside
// the repo: on the exFAT drive macOS adds "._*" files that break the key ring.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISK_MOUNT_PATH")) && builder.Environment.IsDevelopment())
{
    Environment.SetEnvironmentVariable("DISK_MOUNT_PATH",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".myquickmenu"));
}
var diskMount = Environment.GetEnvironmentVariable("DISK_MOUNT_PATH") ?? "/var/data";

// 1. VALIDATE CONNECTION STRING FIRST
// Never commit it. Production reads the ConnectionStrings__DefaultConnection environment
// variable on Render; local development reads appsettings.Development.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "No database connection string. Set the ConnectionStrings__DefaultConnection environment variable " +
        "(Render: Environment tab) or ConnectionStrings:DefaultConnection in appsettings.Development.json for local runs.");
}

// 2. Register Soft Delete Interceptor
builder.Services.AddScoped<SoftDeleteInterceptor>();

// 3. DbContext with RETRY POLICY
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null))
    .AddInterceptors(builder.Services.BuildServiceProvider().GetRequiredService<SoftDeleteInterceptor>()));

builder.Services.AddScoped<ColorExtractionService>();

// 4. Identity Services
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Invite links (set your password) stay valid for 3 days.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(3));

// Keys that sign the login cookie and invite links. Without persisting them, every
// deploy or restart on Render signs everyone out and breaks unused invite links.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(diskMount, "keys")))
    .SetApplicationName("RestaurantMenu");

// Email: SMTP settings come from environment variables (Smtp__Host, Smtp__Port,
// Smtp__User, Smtp__Password, Smtp__From). Without them, invites are shown as a link to copy.
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

// 5. Controllers. Users given a temporary password must replace it before anything else.
builder.Services.AddControllersWithViews(options => options.Filters.Add<RequirePasswordChangeFilter>());
builder.Services.AddScoped<RequirePasswordChangeFilter>();

// 5a. SEO. Render terminates TLS at its proxy and forwards plain HTTP, so without
// this the app sees Scheme == "http" and every canonical, og:url and sitemap entry
// would advertise an http:// URL that immediately redirects — a needless hop that
// also splits signals between the two schemes. KnownProxies is cleared because
// Render's proxy address is not fixed and not knowable ahead of time.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Generate lowercase URLs so links, canonicals and the sitemap agree on one spelling.
builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SeoService>();
builder.Services.AddScoped<QrCodeService>();

// Menu analytics: anonymous events (no cookies, no IP or device stored), owner reports,
// and a daily cleanup of old events.
builder.Services.AddSingleton<MenuAnalytics>();
builder.Services.AddScoped<MenuInsights>();
builder.Services.AddHostedService<AnalyticsRetentionService>();

// Translate-assist on the dish and category forms (Gemini API free tier; see TranslationService).
builder.Services.AddHttpClient<TranslationService>(c => c.Timeout = TimeSpan.FromSeconds(45));

// The public event endpoint takes anonymous posts, so cap them per client address
// (held in memory for the window only, never stored) to keep anyone from inflating
// a restaurant's numbers. A guest browsing normally sends a few per minute.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("menu-events", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Translate-assist, per signed-in owner: plenty for filling in a menu, and it keeps
    // one account from using up the free translation quota for everyone.
    options.AddPolicy("translate", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// 6. Cookie settings
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/home/status/403";
});

// 7. Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
    options.AddPolicy("OwnerOnly", policy => policy.RequireRole("OWNER"));
    options.AddPolicy("AdminOrOwner", policy => policy.RequireRole("ADMIN", "OWNER"));
});

var app = builder.Build();

// Must run before anything reads Request.Scheme or Request.Host — including
// UseHttpsRedirection and every SEO URL the views build.
app.UseForwardedHeaders();

// This is where ALL uploaded images will live (persistent disk)
var persistentImagesRoot = Path.Combine(diskMount, "images");
Directory.CreateDirectory(persistentImagesRoot);

// Serve persistent images at /images/...
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(persistentImagesRoot),
    RequestPath = "/images"
});


// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Friendly pages for 404s and other status codes (e.g. a menu link that no longer exists).
app.UseStatusCodePagesWithReExecute("/home/status/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

// 🔥 CRITICAL: Authentication BEFORE Authorization
app.UseAuthentication();
app.UseAuthorization();

// Custom menu route BEFORE default
app.MapControllerRoute(
    name: "menu",
    pattern: "menu/{branchName}",
    defaults: new { controller = "Menu", action = "Index" }
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

// Apply pending migrations before serving anything. Not caught on purpose: if the
// schema can't be brought up to date the app must not start (Render then keeps the
// previous version live). Set Database__AutoMigrate=false to manage migrations by hand.
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    await DatabaseMigrator.MigrateAsync(app.Services, app.Logger);
}

// Seed database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await DbInitializer.Initialize(services);
        Console.WriteLine("✅ Database seeded successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Seed failed: {ex.Message}");
    }
}

app.Run();
