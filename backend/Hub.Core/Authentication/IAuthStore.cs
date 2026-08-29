namespace Hub.Core.Authentication;

/// <summary>
/// Tầng lưu trữ cho xác thực. Hiện thực nằm ở Hub.Data (EF Core + SQLite) —
/// Core không biết gì về EF (§3, luật phụ thuộc).
/// </summary>
public interface IAuthStore
{
    Task<Credential?> GetCredentialAsync(CancellationToken cancellationToken = default);

    Task SaveCredentialAsync(Credential credential, CancellationToken cancellationToken = default);

    Task<Session?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken cancellationToken = default);

    Task AddSessionAsync(Session session, CancellationToken cancellationToken = default);

    Task UpdateSessionAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>Thu hồi một phiên. Trả về false nếu không tìm thấy hoặc đã thu hồi.</summary>
    Task<bool> RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thu hồi mọi phiên, trừ <paramref name="exceptSessionId"/> nếu có.
    /// Đây là "đăng xuất tất cả thiết bị" của §6.3.
    /// </summary>
    Task<int> RevokeAllSessionsAsync(
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default);

    Task RecordFailedLoginAsync(
        string? tailnetAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Số lần đăng nhập sai kể từ <paramref name="since"/>, để tính khoá tăng dần.</summary>
    Task<int> CountFailedLoginsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    Task ClearFailedLoginsAsync(CancellationToken cancellationToken = default);
}
