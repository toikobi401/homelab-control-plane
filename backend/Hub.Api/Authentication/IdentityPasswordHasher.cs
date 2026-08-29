using Hub.Core.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Hub.Api.Authentication;

/// <summary>
/// Hiện thực băm mật khẩu bằng <see cref="PasswordHasher{TUser}"/> của ASP.NET
/// Core Identity — PBKDF2 với tham số mặc định hiện hành (§6.3).
///
/// §6.1: KHÔNG tự viết crypto. Lớp này chỉ chuyển tiếp sang thư viện, không có
/// một dòng mật mã nào tự nghĩ ra.
/// </summary>
public sealed class IdentityPasswordHasher : Hub.Core.Authentication.IPasswordHasher
{
    // PasswordHasher<T> cần một kiểu user; hệ thống chỉ có một người dùng nên
    // truyền một đối tượng giả — nó không đọc gì từ đối tượng này.
    private static readonly object User = new();

    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(User, password);

    public Hub.Core.Authentication.PasswordVerificationResult Verify(string hash, string password)
    {
        return _hasher.VerifyHashedPassword(User, hash, password) switch
        {
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success
                => Hub.Core.Authentication.PasswordVerificationResult.Success,
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded
                => Hub.Core.Authentication.PasswordVerificationResult.SuccessRehashNeeded,
            _ => Hub.Core.Authentication.PasswordVerificationResult.Failed
        };
    }
}
