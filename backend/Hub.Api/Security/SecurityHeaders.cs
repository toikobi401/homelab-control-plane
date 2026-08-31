namespace Hub.Api.Security;

/// <summary>
/// Header bảo mật cho phản hồi HTTP.
///
/// Khi hệ thống chỉ vào được qua tailnet, những header này gần như thừa — không
/// có kẻ lạ nào ở giữa. Ra Internet thì chúng là lớp phòng thủ thật.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseHubSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Chặn nhúng hub vào iframe của trang khác — chống clickjacking:
            // trang độc phủ một nút trong suốt lên nút "Tắt máy" của ta.
            //
            // Lưu ý chiều ngược lại: hub NHÚNG MeshCentral, và điều đó do
            // frame-ancestors bên MeshCentral quyết định, không phải header này.
            headers["X-Frame-Options"] = "DENY";

            // Trình duyệt không được tự đoán kiểu file. Không có nó, một file
            // người dùng tải lên có thể bị chạy như script.
            headers["X-Content-Type-Options"] = "nosniff";

            // Không rò đường dẫn nội bộ sang trang ngoài qua Referer.
            headers["Referrer-Policy"] = "no-referrer";

            // Tắt các quyền nhạy cảm mà hub không dùng tới.
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

            // HSTS: buộc trình duyệt chỉ dùng HTTPS cho tên miền này trong một
            // năm. Chỉ đặt trên kết nối HTTPS — đặt trên HTTP là vô nghĩa và
            // gây nhầm lẫn khi chạy dev.
            //
            // KHÔNG có `preload`: nó nạp tên miền vào danh sách cứng của trình
            // duyệt, gỡ ra rất chậm. Với hệ thống cá nhân thì không đáng.
            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            await next();
        });
    }
}
