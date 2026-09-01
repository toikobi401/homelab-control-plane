using Hub.Core.Devices;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Hub.Api.Devices;

/// <summary>
/// Cho frontend biết nhúng MeshCentral ở đâu.
///
/// Địa chỉ đến từ cấu hình chứ không hardcode trong React: nó khác nhau giữa
/// máy dev và máy thật, và §3.3 quy định mọi cấu hình đọc từ biến môi trường
/// hoặc appsettings.
/// </summary>
public static class MeshCentralEndpoints
{
    public static IEndpointRouteBuilder MapMeshCentralEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/meshcentral/config", GetConfig)
            .RequireAuthorization()
            .WithTags("MeshCentral")
            .WithName("GetMeshCentralConfig")
            .WithSummary("Địa chỉ MeshCentral để nhúng vào giao diện");

        return builder;
    }

    private static Ok<MeshCentralConfigDto> GetConfig(
        HttpContext httpContext,
        IOptions<MeshCentralOptions> options)
    {
        var settings = options.Value;

        // Chọn địa chỉ theo lối vào: tên MagicDNS chỉ phân giải được từ thiết bị
        // trong tailnet, nên trả nó cho người vào qua Internet công khai là đưa
        // một địa chỉ chắc chắn hỏng.
        var url = settings.ResolveUrl(IsTailnetRequest(httpContext));

        return TypedResults.Ok(new MeshCentralConfigDto(settings.IsConfigured, url));
    }

    /// <summary>
    /// Request có đi qua tailnet không, suy từ Host mà trình duyệt gọi tới.
    ///
    /// Dùng Host chứ không dùng địa chỉ IP của client: sau Cloudflare Tunnel mọi
    /// request đều đến từ loopback, nên IP không phân biệt được lối vào. Host thì
    /// giữ nguyên đúng cái người dùng gõ — và đó cũng chính là thứ quyết định
    /// trình duyệt của họ phân giải được tên nào.
    /// </summary>
    private static bool IsTailnetRequest(HttpContext httpContext)
    {
        var host = httpContext.Request.Host.Host;

        return host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("100.", StringComparison.Ordinal)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }
}

/// <param name="Configured">Đã khai địa chỉ MeshCentral chưa.</param>
/// <param name="Url">Địa chỉ hợp với lối vào hiện tại — dùng cho iframe.</param>
public sealed record MeshCentralConfigDto(bool Configured, string? Url);
