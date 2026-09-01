using Hub.Api.Authentication;
using Hub.Api.Contracts;
using Hub.Api.Devices;
using Hub.Api.Hosting;
using Hub.Api.Security;
using Hub.Core.Abstractions;
using Hub.Core.Authentication;
using Hub.Core.Configuration;
using Hub.Core.Devices;
using Hub.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Docker healthcheck gọi lại chính binary này với --healthcheck, thay vì cài
// curl vào ảnh runtime. Chạy trước khi dựng host: chỉ là một lần gọi HTTP rồi thoát.
if (args.Contains("--healthcheck"))
{
    return await HealthCheckClient.RunAsync();
}

// Content root theo vị trí binary, không theo thư mục làm việc lúc gọi. Mặc định
// của .NET là thư mục hiện tại, nên chạy exe từ chỗ khác — hoặc chạy như Windows
// Service, nơi thư mục làm việc là system32 (§3) — sẽ không tìm thấy wwwroot và
// toàn bộ frontend trả 404. Đã gặp thật khi chạy thử bản publish.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// CONTEXT.md §4: backend không được phơi ra Wi-Fi nhà. Cách thực thi khác nhau
// giữa chạy thẳng trên máy và chạy trong container — xem BindMode.
var bindMode = NetworkBinding.ResolveMode(builder.Configuration);
NetworkBinding.Apply(builder, bindMode);

// §3.3: đường dẫn dữ liệu đọc từ cấu hình, không hardcode. Trong container là
// /data (volume); trên Windows lúc dev rơi về LocalApplicationData.
var dataDirectory = HubPaths.ResolveDataDirectory(
    builder.Configuration[HubPaths.DataDirectoryKey]);
Directory.CreateDirectory(dataDirectory);

var databasePath = Path.Combine(dataDirectory, "hub.db");

// Đặt lại mật khẩu khi quên mật khẩu cũ. Chạy trên chính máy có file DB nên
// không cần dựng web host — xem PasswordResetTool để biết vì sao nó không phá
// quy tắc "đổi mật khẩu phải nhập mật khẩu cũ" của §6.3.
if (args.Contains(PasswordResetTool.Flag))
{
    return await PasswordResetTool.RunAsync(args, databasePath);
}

builder.Services.AddDbContext<HubDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services.Configure<AuthOptions>(
    builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<SetupOptions>(
    builder.Configuration.GetSection(SetupOptions.SectionName));
builder.Services.Configure<TailscaleOptions>(
    builder.Configuration.GetSection(TailscaleOptions.SectionName));
builder.Services.Configure<MeshCentralOptions>(
    builder.Configuration.GetSection(MeshCentralOptions.SectionName));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<Hub.Core.Authentication.IPasswordHasher, IdentityPasswordHasher>();
builder.Services.AddScoped<IAuthStore, EfAuthStore>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<LocalSetupPolicy>();
builder.Services.AddScoped<AntiforgeryFilter>();

// Năng lực 1 — đọc thiết bị từ Tailscale.
// HttpClient qua factory, không new thủ công (§3: tránh cạn socket).
builder.Services.AddHttpClient(TailscaleClient.HttpClientName, client =>
{
    // Tailscale ở ngoài tailnet nên có thể chậm; timeout để không treo request
    // của người dùng (§5a quy tắc 6).
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Singleton: token dùng chung cho mọi request, hết hạn sau một giờ.
builder.Services.AddSingleton<TailscaleTokenProvider>();
builder.Services.AddSingleton<TailscaleClient>();

// Cache đứng trước client thật — mọi chỗ khác chỉ thấy ITailnetClient.
builder.Services.AddSingleton<ITailnetClient, CachedTailnetClient>();

builder.Services.AddHubAuthentication();

// §6.5 mục 5: SameSite=Strict chặn phần lớn CSRF, nhưng endpoint đổi trạng thái
// vẫn phải có antiforgery token.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "hub_csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Rate limit — cần khi mở ra Internet. Tailnet vốn che việc này: chỉ thiết bị
// đã cài Tailscale mới gọi tới được, nên không ai dội request vào hệ thống.
builder.Services.AddHubRateLimiting();

// Sau Cloudflare Tunnel, TLS kết thúc ở biên và hub nhận HTTP trên loopback.
// Không đọc X-Forwarded-Proto thì cookie antiforgery (Secure) ném lỗi và mọi
// POST trả 500. Chỉ bật ở chế độ Tunnel — xem ForwardedHeadersSetup.
builder.Services.AddTunnelForwardedHeaders(bindMode);

// Phase 0: frontend sinh kiểu TypeScript từ spec này (§3).
builder.Services.AddOpenApi();

var app = builder.Build();

// Nâng cấp schema lúc khởi động bằng EF migration.
//
// Trước đây dùng EnsureCreated: nó CHỈ tạo DB mới, không nâng cấp DB đã tồn tại.
// Hệ quả là thêm bảng mới (Devices cho năng lực 6) thì DB cũ vẫn thiếu bảng và
// chết lúc chạy với "no such table". Migration sửa đúng gốc vấn đề đó.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    await DatabaseInitializer.MigrateAsync(dbContext, app.Logger);
}

app.Logger.LogInformation(
    "Hub khởi động — chế độ bind {BindMode}, thư mục dữ liệu {DataDirectory}",
    bindMode, dataDirectory);

// §6.5 mục 7: không hiện chi tiết lỗi ra frontend. Stack trace vào log,
// người dùng chỉ thấy thông báo chung.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { title = "Đã có lỗi xảy ra." });
}));

// §3: frontend build ra file tĩnh và do chính backend này phục vụ — không dựng
// web server thứ hai. wwwroot có thể chưa tồn tại khi chạy backend mà chưa build
// frontend; khi đó chỉ đơn giản là không có file nào để phục vụ.
//
// Phải đứng TRƯỚC routing: đặt sau các MapGet thì endpoint fallback khớp trước,
// và mọi file js/css bị trả về index.html với MIME text/html — trình duyệt từ
// chối nạp module. Đã gặp thật, không phải phòng xa.
// PHẢI đứng trước mọi middleware khác: nó sửa lại Request.IsHttps và
// RemoteIpAddress từ header chuyển tiếp, mà cả header bảo mật lẫn rate limit
// đều dựa vào hai giá trị đó.
app.UseForwardedHeaders();

// Header bảo mật đặt sớm để áp cho MỌI phản hồi, kể cả file tĩnh và trang lỗi.
app.UseHubSecurityHeaders();

// Rate limit trước static files: request dội vào file tĩnh cũng phải bị chặn,
// không thì bot vẫn làm nghẽn được máy bằng cách tải index.html liên tục.
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Endpoint /health — Phase 0. Không yêu cầu xác thực: dùng để kiểm tra
// backend còn sống từ điện thoại qua tailnet, và để Docker healthcheck gọi.
// Trả record đặt tên, không phải anonymous object: OpenAPI cần kiểu có tên để
// sinh schema, và frontend sinh kiểu TypeScript từ schema đó (§3).
app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("GetHealth")
    .WithSummary("Trạng thái sống của backend");

app.MapAntiforgeryEndpoints();
app.MapAuthEndpoints();
app.MapDeviceEndpoints();
app.MapMeshCentralEndpoints();

// React Router điều hướng phía client: /devices không có file tương ứng trên đĩa.
// Fallback trả index.html để router tự xử lý đường dẫn.
//
// Chặn {**path} không bắt đầu bằng "api/": nếu không, gọi nhầm một endpoint API
// sẽ nhận 200 kèm index.html thay vì 404. Frontend khi đó chết ở JSON.parse với
// lỗi không liên quan gì tới nguyên nhân thật. Đã kiểm chứng bằng thực nghiệm.
app.MapFallbackToFile("index.html");

// Gọi nhầm một endpoint API phải nhận 404, không phải index.html: nếu không,
// frontend chết ở JSON.parse với lỗi chẳng liên quan gì tới nguyên nhân thật.
// Đặt sau fallback nhưng route cụ thể hơn nên thắng. Đã kiểm chứng bằng thực nghiệm.
app.MapFallback("/api/{**path}", () => Results.NotFound());

await app.RunAsync();
return 0;
