namespace Hub.Core.Authentication;

/// <summary>
/// Băm và kiểm mật khẩu. Hiện thực dùng <c>PasswordHasher</c> của ASP.NET Core
/// Identity (§6.3) — Core không tham chiếu ASP.NET nên đứng sau interface này.
///
/// §6.1: KHÔNG tự viết crypto. Hiện thực chỉ được gọi thư viện có sẵn.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string hash, string password);
}

public enum PasswordVerificationResult
{
    Failed = 0,
    Success = 1,

    /// <summary>Đúng mật khẩu, nhưng hash dùng tham số cũ — nên băm lại.</summary>
    SuccessRehashNeeded = 2
}
