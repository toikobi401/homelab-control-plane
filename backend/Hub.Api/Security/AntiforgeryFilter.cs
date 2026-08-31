using Microsoft.AspNetCore.Antiforgery;

namespace Hub.Api.Security;

/// <summary>
/// Bắt buộc kiểm tra antiforgery token cho endpoint đổi trạng thái (§6.5 mục 5).
///
/// Vì sao cần filter này thay vì chỉ gọi <c>UseAntiforgery()</c>: middleware đó
/// chỉ **tự động** validate cho request có form binding. Minimal API nhận JSON
/// thì nó bỏ qua hoàn toàn — đã kiểm chứng bằng HTTP thật: POST /api/auth/setup
/// không kèm token vẫn trả 204.
///
/// §6.5 mục 5 nói SameSite=Strict chặn phần lớn CSRF, nhưng endpoint đổi trạng
/// thái **vẫn phải** có token — và năng lực 6 (tắt máy từ xa) là nơi nói rõ
/// "đặc biệt cần điều này".
/// </summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // Không nói chi tiết vì sao token sai (§6.5 mục 7).
            return Results.Problem(
                title: "Yêu cầu thiếu hoặc sai CSRF token.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}

public static class AntiforgeryEndpointExtensions
{
    /// <summary>
    /// Gắn kiểm tra antiforgery. Dùng cho MỌI endpoint đổi trạng thái.
    ///
    /// Ngoại lệ có chủ đích: endpoint agent tự đăng ký không dùng cookie phiên
    /// mà xác thực bằng khoá chung, nên CSRF không áp dụng — trình duyệt không
    /// có khoá đó để tự gửi kèm.
    /// </summary>
    public static RouteHandlerBuilder RequireAntiforgery(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<AntiforgeryFilter>();

    public static RouteGroupBuilder RequireAntiforgery(this RouteGroupBuilder builder)
        => builder.AddEndpointFilter<AntiforgeryFilter>();
}
