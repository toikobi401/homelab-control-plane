using System.Security.Cryptography;
using System.Text;
using Hub.Agent;
using Hub.Core.Devices;
using Hub.Windows;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AgentSettings>(
    builder.Configuration.GetSection(AgentSettings.SectionName));

var settings = builder.Configuration
    .GetSection(AgentSettings.SectionName)
    .Get<AgentSettings>() ?? new AgentSettings();

// Agent chỉ phục vụ backend qua tailnet. Bind mọi địa chỉ ở đây là để backend
// gọi tới được; ranh giới bảo vệ là khoá chung bên dưới, không phải địa chỉ —
// §6.4: không bao giờ tin "gọi từ 100.x nên bỏ qua xác thực".
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(settings.Port));

builder.Services.AddHttpClient("hub", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Backend dùng chứng chỉ dev tự ký lúc phát triển. Agent chạy trong tailnet
    // (đã mã hoá đầu-cuối) nên chấp nhận được; khi có tailscale cert thì bỏ.
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddHostedService<RegistrationWorker>();

// Mã riêng cho Windows nằm sau interface (§3.3). Máy khác Windows thì agent vẫn
// chạy và báo danh được, chỉ là không nhận lệnh điều khiển nguồn.
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IPowerController, WindowsPowerController>();
}

var app = builder.Build();

app.MapGet("/agent/health", () => Results.Ok(new { status = "ok" }));

// §5a điều 1 & 2: nhận đúng một hành động có kiểu, qua POST, body JSON.
// Không có đường nào để một chuỗi lệnh tuỳ ý đi tới chỗ thực thi.
app.MapPost("/agent/power", (
    PowerRequest request,
    HttpContext httpContext,
    IOptions<AgentSettings> agentSettings,
    IServiceProvider services,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("AgentPower");
    var expected = agentSettings.Value.SharedSecret;

    if (string.IsNullOrWhiteSpace(expected))
    {
        logger.LogError("Chưa cấu hình Agent:SharedSecret — từ chối mọi lệnh.");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    if (!IsAuthorized(httpContext, expected))
    {
        logger.LogWarning(
            "Từ chối lệnh: khoá chung sai (từ {Address}).",
            httpContext.Connection.RemoteIpAddress);

        return Results.Unauthorized();
    }

    // Chỉ nhận đúng các giá trị trong enum. Chuỗi lạ bị từ chối ngay, không có
    // nhánh mặc định nào chạy thứ gì đó.
    if (!Enum.TryParse<PowerAction>(request.Action, ignoreCase: true, out var action))
    {
        logger.LogWarning("Hành động không hợp lệ: {Action}", request.Action);
        return Results.BadRequest(new { title = "Hành động không hợp lệ." });
    }

    var controller = services.GetService<IPowerController>();
    if (controller is null)
    {
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
    }

    logger.LogWarning("Thực thi lệnh {Action}.", action);

    var result = controller.Execute(action);

    return result.IsSuccess
        ? Results.NoContent()
        : Results.Problem(
            title: result.Error!.Value.Message,
            statusCode: StatusCodes.Status500InternalServerError);
});

app.Run();

/// <summary>
/// So sánh khoá theo thời gian cố định — so sánh chuỗi thường thoát sớm ở byte
/// đầu khác nhau, để lộ thông tin qua thời gian phản hồi.
/// </summary>
static bool IsAuthorized(HttpContext httpContext, string expected)
{
    var header = httpContext.Request.Headers.Authorization.ToString();

    const string prefix = "Bearer ";
    if (!header.StartsWith(prefix, StringComparison.Ordinal))
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(header[prefix.Length..]),
        Encoding.UTF8.GetBytes(expected));
}

internal sealed record PowerRequest(string Action);
