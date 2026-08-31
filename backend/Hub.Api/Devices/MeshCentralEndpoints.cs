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

    private static Ok<MeshCentralConfigDto> GetConfig(IOptions<MeshCentralOptions> options)
    {
        var settings = options.Value;

        return TypedResults.Ok(new MeshCentralConfigDto(
            settings.IsConfigured,
            settings.Url));
    }
}

public sealed record MeshCentralConfigDto(bool Configured, string? Url);
