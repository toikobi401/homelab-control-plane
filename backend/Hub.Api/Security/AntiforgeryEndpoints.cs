using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Hub.Api.Security;

public static class AntiforgeryEndpoints
{
    public static IEndpointRouteBuilder MapAntiforgeryEndpoints(this IEndpointRouteBuilder builder)
    {
        // Client gọi endpoint này một lần lúc khởi động để lấy token, rồi gửi
        // kèm header X-CSRF-Token cho mọi POST/DELETE sau đó.
        //
        // AllowAnonymous: màn hình đặt mật khẩu lần đầu và đăng nhập cũng cần
        // token, mà lúc đó chưa có phiên nào.
        builder.MapGet("/api/antiforgery/token", GetToken)
            .AllowAnonymous()
            .WithTags("Security")
            .WithName("GetAntiforgeryToken")
            .WithSummary("Lấy CSRF token cho các request đổi trạng thái");

        return builder;
    }

    private static Ok<AntiforgeryTokenDto> GetToken(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        // GetAndStoreTokens đặt cookie phần bí mật và trả về phần token gửi kèm
        // header. Hai nửa phải khớp nhau thì request mới hợp lệ — đó là cách
        // chống CSRF: trang khác không đọc được cookie để lấy nửa còn lại.
        var tokens = antiforgery.GetAndStoreTokens(httpContext);

        return TypedResults.Ok(new AntiforgeryTokenDto(
            tokens.RequestToken ?? "",
            tokens.HeaderName ?? "X-CSRF-Token"));
    }
}

public sealed record AntiforgeryTokenDto(string Token, string HeaderName);
