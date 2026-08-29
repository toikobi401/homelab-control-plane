using Hub.Core.Abstractions;
using Hub.Core.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Hub.Data;

/// <summary>
/// Hiện thực <see cref="IAuthStore"/> bằng EF Core. Toàn bộ kiến thức về EF
/// dừng lại ở đây — Hub.Core không biết gì (§3, luật phụ thuộc).
/// </summary>
public sealed class EfAuthStore(HubDbContext dbContext, IClock clock) : IAuthStore
{
    public async Task<Credential?> GetCredentialAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(credential => credential.Id == 1, cancellationToken);
    }

    public async Task SaveCredentialAsync(
        Credential credential,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Credentials
            .FirstOrDefaultAsync(row => row.Id == credential.Id, cancellationToken);

        if (existing is null)
        {
            dbContext.Credentials.Add(credential);
        }
        else
        {
            existing.PasswordHash = credential.PasswordHash;
            existing.UpdatedAt = credential.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Session?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Sessions
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        return await dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.RevokedAt == null && session.ExpiresAt > now)
            .OrderByDescending(session => session.LastSeenAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        // Session lấy ra từ GetSessionAsync đã được tracking, nên chỉ cần lưu.
        // Attach lại phòng trường hợp người gọi truyền thực thể rời.
        if (dbContext.Entry(session).State == EntityState.Detached)
        {
            dbContext.Sessions.Update(session);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RevokeSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Sessions
            .FirstOrDefaultAsync(row => row.Id == sessionId, cancellationToken);

        if (session is null || session.RevokedAt is not null)
        {
            return false;
        }

        // Giữ lại dòng, chỉ đánh dấu — để còn dấu vết kiểm toán (§6.3).
        session.RevokedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RevokeAllSessionsAsync(
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var query = dbContext.Sessions
            .Where(session => session.RevokedAt == null && session.ExpiresAt > now);

        if (exceptSessionId is not null)
        {
            query = query.Where(session => session.Id != exceptSessionId.Value);
        }

        // ExecuteUpdate: một câu UPDATE, không nạp toàn bộ phiên vào bộ nhớ.
        return await query.ExecuteUpdateAsync(
            setters => setters.SetProperty(session => session.RevokedAt, now),
            cancellationToken);
    }

    public async Task RecordFailedLoginAsync(
        string? tailnetAddress,
        CancellationToken cancellationToken = default)
    {
        dbContext.FailedLoginAttempts.Add(new FailedLoginAttempt
        {
            AttemptedAt = clock.UtcNow,
            TailnetAddress = tailnetAddress
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountFailedLoginsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.FailedLoginAttempts
            .AsNoTracking()
            .CountAsync(attempt => attempt.AttemptedAt >= since, cancellationToken);
    }

    public async Task ClearFailedLoginsAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.FailedLoginAttempts.ExecuteDeleteAsync(cancellationToken);
    }
}
