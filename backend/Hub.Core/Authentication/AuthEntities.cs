namespace Hub.Core.Authentication;

/// <summary>
/// Thông tin xác thực của người dùng duy nhất (§6.3: một người dùng, không có
/// bảng nhiều dòng, không vai trò).
///
/// Chỉ lưu hash — không bao giờ lưu mật khẩu thô, kể cả tạm thời.
/// </summary>
public sealed class Credential
{
    /// <summary>Luôn là 1 — hệ thống chỉ có một người dùng.</summary>
    public int Id { get; set; } = 1;

    /// <summary>Hash sinh bởi <c>PasswordHasher</c> của ASP.NET Core Identity.</summary>
    public required string PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Một phiên đăng nhập (§6.3). Lưu trong DB chứ không chỉ nằm trong cookie đã
/// ký — đây là điều kiện để thu hồi được khi mất thiết bị.
/// </summary>
public sealed class Session
{
    public required Guid Id { get; set; }

    /// <summary>Mô tả thiết bị, lấy từ User-Agent. Chỉ để người dùng nhận ra máy nào.</summary>
    public required string Device { get; set; }

    /// <summary>IP tailnet lúc tạo phiên. Dùng cho màn hình liệt kê phiên.</summary>
    public string? TailnetAddress { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm bị thu hồi. Giữ lại dòng đã thu hồi thay vì xoá để còn dấu vết
    /// kiểm toán; mọi truy vấn phải lọc theo trường này.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>
/// Một lần đăng nhập thất bại (§6.3: ghi nhật ký mọi lần thất bại).
/// KHÔNG lưu mật khẩu đã nhập, kể cả khi sai.
/// </summary>
public sealed class FailedLoginAttempt
{
    public int Id { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }

    public string? TailnetAddress { get; set; }
}
