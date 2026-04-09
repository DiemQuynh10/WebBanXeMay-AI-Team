using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using WebBanXeMay.Hubs;
using WebBanXeMay.Configurations;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var toolApiKey = SharedEnvLoader.GetValue(
    builder.Environment.ContentRootPath,
    "TOOL_API_KEY",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(toolApiKey))
{
    builder.Configuration["ToolApi:ApiKey"] = toolApiKey;
}
// ===== Database (SQL Server) =====
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();

builder.Services.AddOptions<ToolApiOptions>()
    .Bind(builder.Configuration.GetSection("ToolApi"))
    .ValidateDataAnnotations()
    .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "ToolApi:ApiKey is required.")
    .ValidateOnStart();

// ===== Identity + Default UI (Razor Pages) =====
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// ===== Authorization: policy cho khu vực Admin =====
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
});

// ===== Cookie redirect rules =====
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";

    options.Events = new CookieAuthenticationEvents
    {
        // Chuyển hướng khi CHƯA đăng nhập
        OnRedirectToLogin = ctx =>
        {
            if (IsAjaxOrApi(ctx.Request))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            if (!HttpMethods.IsGet(ctx.Request.Method))
            {
                var safeReturn = "/cart";
                var login = $"{options.LoginPath}?returnUrl={Uri.EscapeDataString(safeReturn)}";
                ctx.Response.Redirect(login);
                return Task.CompletedTask;
            }

            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        },

        // Chuyển hướng khi KHÔNG đủ quyền
        OnRedirectToAccessDenied = ctx =>
        {
            if (IsAjaxOrApi(ctx.Request))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        }
    };
});

// Helper nhận diện AJAX/API
static bool IsAjaxOrApi(HttpRequest req)
{
    if (req.Headers.TryGetValue("X-Requested-With", out var xhr)
        && string.Equals(xhr.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        return true;

    if (req.Headers.TryGetValue("Accept", out var accept) &&
        accept.Any(a => a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase)))
        return true;

    if (req.Path.StartsWithSegments("/api"))
        return true;

    return false;
}

// ===== MVC + Razor Pages =====
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ===== SIGNALR SERVICE =====
builder.Services.AddSignalR(); // <== THÊM DÒNG NÀY (Đăng ký dịch vụ SignalR)

// ===== Session =====
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(12);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

// ================================================================
// BUILDER HOÀN TẤT
// ================================================================
var app = builder.Build();

// ===== Seed dữ liệu (roles/users) =====
// using (var scope = app.Services.CreateScope())
// {
//     var sp = scope.ServiceProvider;
//     var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
//     var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
//     var db = sp.GetRequiredService<AppDbContext>();

//     try
//     {
//         await DataSeeder.SeedAsync(userMgr, roleMgr, db);
//     }
//     catch (Exception ex)
//     {
//         var logger = sp.GetRequiredService<ILogger<Program>>();
//         logger.LogError(ex, "Đã xảy ra lỗi khi seed data.");
//     }
// }

// ================================================================
// CONFIGURE HTTP PIPELINE
// ================================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// MAP CÁC ENDPOINTS

app.MapRazorPages();

// Route Area Admin
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// Route Mặc định (User)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ===== MAP SIGNALR HUB =====
app.MapHub<ChatHub>("/chatHub"); // <== THÊM DÒNG NÀY (Định tuyến đường dẫn cho Hub)

app.Run();

static class SharedEnvLoader
{
    public static string? GetValue(string contentRootPath, string key, string envFilePath)
    {
        var systemValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(systemValue))
        {
            return systemValue.Trim();
        }

        if (!File.Exists(envFilePath))
        {
            return null;
        }

        foreach (var rawLine in File.ReadAllLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var currentKey = line[..separatorIndex].Trim();
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}