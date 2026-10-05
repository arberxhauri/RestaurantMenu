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
// The interceptor comes from the context's own scope (not a second, throwaway service
// provider built here), so it shares the app's singletons.
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options.UseNpgsql(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null))
    .AddInterceptors(sp.GetRequiredService<SoftDeleteInterceptor>()));

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
    // Emailed links: invites (3 days) and password resets (2 hours), see AccountTokens.
    options.Tokens.EmailConfirmationTokenProvider = AccountTokens.Invite;
    options.Tokens.PasswordResetTokenProvider = AccountTokens.PasswordReset;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddTokenProvider<InviteTokenProvider>(AccountTokens.Invite)
.AddTokenProvider<PasswordResetTokenProvider>(AccountTokens.PasswordReset);

// The default provider made invite links before AccountTokens existed; AccountController
// still accepts those for their 3 days, so links already sent keep working.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = AccountTokens.InviteLifespan);

// Keys that sign the login cookie and invite links. Without persisting them, every
// deploy or restart on Render signs everyone out and breaks unused invite links.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(diskMount, "keys")))
    .SetApplicationName("RestaurantMenu");

// Email (invites, password resets). Set Email__From plus one provider:
//   Email__ResendApiKey (Resend, HTTPS)   or   Email__BrevoApiKey (Brevo, HTTPS)
//   or Smtp__Host/Port/User/Password (SMTP; blocked on Render's free plan).
// Without any, invite links are shown on screen to copy and "Forgot password" explains
// who to ask. The admin page shows which provider is in use.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddHttpClient<ResendEmailTransport>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<BrevoEmailTransport>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransient<IEmailTransport>(sp => sp.GetRequiredService<ResendEmailTransport>());
builder.Services.AddTransient<IEmailTransport>(sp => sp.GetRequiredService<BrevoEmailTransport>());
builder.Services.AddTransient<IEmailTransport, SmtpEmailTransport>();
builder.Services.AddTransient<EmailService>();
builder.Services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<EmailService>());
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddHostedService<EmailQueueWorker>();
builder.Services.AddMemoryCache();

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
// Who may do what to a branch (owner, or staff member with a role). Used by every back-office controller.
builder.Services.AddScoped<IBranchAccess, BranchAccess>();
builder.Services.AddTransient<InviteMailer>();

// Table ordering: guests' orders go to kitchen displays live over SignalR (WebSockets,
// with fallbacks). One app instance: with several, add a backplane (e.g. Redis).
builder.Services.AddScoped<OrderService>();

// Management: stock (orders use it through recipes), shifts, and end-of-day sales rolled up
// from orders every night (SalesRollupService).
builder.Services.AddScoped<StockService>();
// Guest feedback from the menu footer (low ratings privately to the owner, high ones to Google).
builder.Services.AddScoped<FeedbackService>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddHostedService<SalesRollupService>();

// Bookings: availability, the guest's booking page, the owner's day view, reminders.
// SMS is optional (Sms__TwilioAccountSid/AuthToken/From, or Sms__WebhookUrl for a local
// gateway); without it guests get their confirmation on screen and by email.
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection("Sms"));
builder.Services.AddHttpClient<SmsService>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<BackgroundJobs>();
builder.Services.AddHostedService<BackgroundJobsWorker>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddSingleton<BookingReminderService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BookingReminderService>());

// Restaurant websites: /site/{slug}, and the restaurant's own domain (SiteHostMiddleware).
// Sites__WildcardDomain gives every site a free {restaurant}.yourdomain address;
// Sites__RenderApiKey + Sites__RenderServiceId register own domains with Render automatically.
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Sites"));
builder.Services.AddSingleton<SiteHosts>();
builder.Services.AddHttpClient<DomainService>(c => c.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<DomainCheckService>();
builder.Services.AddSignalR();

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

    // "Forgot password", per client address: enough for a few typos, not for spraying
    // reset emails at a list of addresses. Each account also gets at most one email
    // every 2 minutes (AccountController).
    options.AddPolicy("forgot-password", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));

    // Table orders, per client address. Generous because a restaurant's guests often share
    // one Wi-Fi address; each table is limited separately as well (OrderService).
    // Orders__RateLimitPerIp raises it for a very busy venue on one connection.
    var ordersPerIp = Math.Max(5, builder.Configuration.GetValue("Orders:RateLimitPerIp", 40));
    options.AddPolicy("orders", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = ordersPerIp, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));

    // Online bookings, per client address: a family booking a few evenings, not a script
    // filling the restaurant with fake bookings (one phone may also hold only 3 at a time).
    options.AddPolicy("bookings", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(3, builder.Configuration.GetValue("Bookings:RateLimitPerIp", 10)), Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));

    // Guest feedback, per client address: a table of friends each rating, not a flood.
    options.AddPolicy("feedback", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(3, builder.Configuration.GetValue("Feedback:RateLimitPerIp", 10)), Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));

    // People get a page that explains, not a bare 429.
    options.OnRejected = (context, _) =>
    {
        var http = context.HttpContext;
        if (http.Request.Path.StartsWithSegments("/account/forgotpassword", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.StatusCode = StatusCodes.Status303SeeOther;
            http.Response.Headers.Location = "/account/forgotpassword?busy=true";
        }
        return ValueTask.CompletedTask;
    };
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

// Restaurants' own domains: before static files and routing, so "/" on such a domain becomes
// that restaurant's website and back-office paths go to the app's own address.
app.UseMiddleware<SiteHostMiddleware>();

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
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // The menu's service worker (offline menus): always revalidated, so a new version is
        // picked up on the next visit instead of whenever the browser's cache expires.
        if (ctx.Context.Request.Path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
            ctx.Context.Response.Headers.ContentType = "text/javascript; charset=utf-8";
        }
    }
});

app.UseRouting();
app.UseRateLimiter();

// 🔥 CRITICAL: Authentication BEFORE Authorization
app.UseAuthentication();
app.UseAuthorization();

// Custom menu route BEFORE default
app.MapHub<RestaurantMenu.Hubs.KitchenHub>("/hubs/kitchen");

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

// Say in the deploy log whether emails will go out, and why not.
using (var scope = app.Services.CreateScope())
{
    var email = scope.ServiceProvider.GetRequiredService<EmailService>();
    if (email.IsConfigured)
        app.Logger.LogInformation("Email: sending through {Provider} from {From}", email.ProviderName, email.Sender!.Formatted);
    else
        app.Logger.LogWarning("Email: off. {Problem} Invite links are shown on screen and password reset by email is unavailable.", email.Problem);
    var sms = scope.ServiceProvider.GetRequiredService<SmsService>();
    app.Logger.LogInformation(sms.IsConfigured ? "SMS: sending through {Provider}" : "SMS: off (booking confirmations by email and on screen){Provider}", sms.ProviderName ?? "");
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
