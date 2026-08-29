using Hub.Core.Abstractions;
using Hub.Core.Authentication;

namespace Hub.Core.Tests;

/// <summary>Đồng hồ điều khiển được, để test hết hạn phiên mà không phải chờ thật.</summary>
internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// Băm giả cho test: KHÔNG phải crypto, chỉ để kiểm logic nghiệp vụ.
/// Bản thật dùng PasswordHasher của ASP.NET Core Identity (§6.1).
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int HashCallCount { get; private set; }

    public string Hash(string password)
    {
        HashCallCount++;
        return "hashed:" + password;
    }

    public PasswordVerificationResult Verify(string hash, string password)
    {
        return hash == "hashed:" + password
            ? PasswordVerificationResult.Success
            : PasswordVerificationResult.Failed;
    }
}

/// <summary>Store trong bộ nhớ. Giữ đúng ngữ nghĩa của bản EF, kể cả việc thu hồi là đánh dấu.</summary>
internal sealed class InMemoryAuthStore(FakeClock clock) : IAuthStore
{
    private readonly List<Session> _sessions = [];
    private readonly List<FailedLoginAttempt> _failures = [];
    private Credential? _credential;

    public Task<Credential?> GetCredentialAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_credential);

    public Task SaveCredentialAsync(Credential credential, CancellationToken cancellationToken = default)
    {
        _credential = credential;
        return Task.CompletedTask;
    }

    public Task<Session?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions.FirstOrDefault(session => session.Id == sessionId));

    public Task<IReadOnlyList<Session>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        IReadOnlyList<Session> active = _sessions.Where(session => session.IsActive(now)).ToList();
        return Task.FromResult(active);
    }

    public Task AddSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        _sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateSessionAsync(Session session, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.FirstOrDefault(row => row.Id == sessionId);
        if (session is null || session.RevokedAt is not null)
        {
            return Task.FromResult(false);
        }

        session.RevokedAt = clock.UtcNow;
        return Task.FromResult(true);
    }

    public Task<int> RevokeAllSessionsAsync(
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var targets = _sessions
            .Where(session => session.IsActive(now) && session.Id != exceptSessionId)
            .ToList();

        foreach (var session in targets)
        {
            session.RevokedAt = now;
        }

        return Task.FromResult(targets.Count);
    }

    public Task RecordFailedLoginAsync(string? tailnetAddress, CancellationToken cancellationToken = default)
    {
        _failures.Add(new FailedLoginAttempt
        {
            AttemptedAt = clock.UtcNow,
            TailnetAddress = tailnetAddress
        });
        return Task.CompletedTask;
    }

    public Task<int> CountFailedLoginsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_failures.Count(attempt => attempt.AttemptedAt >= since));

    public Task ClearFailedLoginsAsync(CancellationToken cancellationToken = default)
    {
        _failures.Clear();
        return Task.CompletedTask;
    }
}
