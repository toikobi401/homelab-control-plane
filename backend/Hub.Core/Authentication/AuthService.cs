using Hub.Core.Abstractions;
using Hub.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Core.Authentication;

/// <summary>
/// Logic xác thực (§6.3). Không biết gì về HTTP, cookie, hay EF — chỉ nghiệp vụ,
/// nên test được bằng xUnit mà không cần dựng web host.
/// </summary>
public sealed class AuthService(
    IAuthStore store,
    IPasswordHasher passwordHasher,
    IClock clock,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger)
{
    private readonly AuthOptions _options = options.Value;

    /// <summary>
    /// Thông báo dùng chung cho mọi kiểu đăng nhập sai. §6.3 yêu cầu không phân
    /// biệt được sai gì; §6.5 mục 7 cấm lộ chi tiết ra frontend.
    /// </summary>
    private const string InvalidCredentialsMessage = "Mật khẩu không đúng.";

    public async Task<AuthStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var credential = await store.GetCredentialAsync(cancellationToken);
        return new AuthStatus(credential is not null);
    }

    /// <summary>
    /// Đặt mật khẩu lần đầu. §6.3: chỉ chấp nhận từ localhost — người gọi
    /// (endpoint) phải tự kiểm tra điều đó trước khi gọi vào đây.
    /// </summary>
    public async Task<Result> SetInitialPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.GetCredentialAsync(cancellationToken);
        if (existing is not null)
        {
            // Không cho đặt đè — đổi mật khẩu phải đi qua ChangePasswordAsync
            // (bắt nhập mật khẩu cũ).
            return Result.Failure(ResultError.Conflict("Mật khẩu đã được đặt."));
        }

        var validation = ValidatePassword(password);
        if (validation.IsFailure)
        {
            return validation;
        }

        var now = clock.UtcNow;
        await store.SaveCredentialAsync(
            new Credential
            {
                Id = 1,
                PasswordHash = passwordHasher.Hash(password),
                CreatedAt = now,
                UpdatedAt = now
            },
            cancellationToken);

        logger.LogInformation("Đã đặt mật khẩu lần đầu.");
        return Result.Success();
    }

    /// <summary>
    /// Đăng nhập. Trả về phiên mới khi thành công.
    ///
    /// §6.3: thời gian phản hồi phải như nhau dù chưa đặt mật khẩu hay sai mật
    /// khẩu — nên trường hợp "chưa có credential" vẫn phải băm một lần giả.
    /// </summary>
    public async Task<Result<LoginSuccess>> LoginAsync(
        string password,
        string device,
        string? tailnetAddress,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var lockout = await GetLockoutAsync(now, cancellationToken);
        if (lockout.IsFailure)
        {
            return Result.Failure<LoginSuccess>(lockout.Error!.Value);
        }

        var credential = await store.GetCredentialAsync(cancellationToken);

        if (credential is null)
        {
            // Chưa đặt mật khẩu. Vẫn băm một lần để thời gian phản hồi giống
            // hệt trường hợp sai mật khẩu — nếu return sớm, kẻ tấn công đo thời
            // gian là biết hệ thống chưa khởi tạo.
            _ = passwordHasher.Hash(password);
            await RecordFailureAsync(tailnetAddress, cancellationToken);
            return Result.Failure<LoginSuccess>(
                ResultError.Unauthorized(InvalidCredentialsMessage));
        }

        var verification = passwordHasher.Verify(credential.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(tailnetAddress, cancellationToken);
            return Result.Failure<LoginSuccess>(
                ResultError.Unauthorized(InvalidCredentialsMessage));
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = passwordHasher.Hash(password);
            credential.UpdatedAt = now;
            await store.SaveCredentialAsync(credential, cancellationToken);
        }

        // Đăng nhập thành công thì xoá lịch sử sai, để lần khoá sau tính lại từ đầu.
        await store.ClearFailedLoginsAsync(cancellationToken);

        // Phiên mới luôn có id mới — chống session fixation (§6.3). Không có
        // đường nào tái dùng id cũ.
        var session = new Session
        {
            Id = Guid.NewGuid(),
            Device = Truncate(device, 200),
            TailnetAddress = tailnetAddress,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + _options.SessionLifetime
        };

        await store.AddSessionAsync(session, cancellationToken);

        logger.LogInformation("Đăng nhập thành công, phiên {SessionId}.", session.Id);
        return Result.Success(new LoginSuccess(session));
    }

    /// <summary>
    /// Kiểm tra phiên còn hiệu lực, đồng thời gia hạn trượt nếu sắp hết hạn.
    /// Gọi mỗi request đã xác thực.
    /// </summary>
    public async Task<Result<Session>> ValidateSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var session = await store.GetSessionAsync(sessionId, cancellationToken);

        if (session is null || !session.IsActive(now))
        {
            return Result.Failure<Session>(
                ResultError.Unauthorized("Phiên không hợp lệ hoặc đã hết hạn."));
        }

        session.LastSeenAt = now;

        // Chỉ ghi DB khi thật sự cần gia hạn — tránh mỗi request một lần ghi.
        var remaining = session.ExpiresAt - now;
        if (remaining < _options.SlidingRenewalThreshold)
        {
            session.ExpiresAt = now + _options.SessionLifetime;
        }

        await store.UpdateSessionAsync(session, cancellationToken);
        return Result.Success(session);
    }

    public Task<IReadOnlyList<Session>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        return store.GetActiveSessionsAsync(cancellationToken);
    }

    public async Task<Result> RevokeSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var revoked = await store.RevokeSessionAsync(sessionId, cancellationToken);
        if (!revoked)
        {
            return Result.Failure(ResultError.Validation("Không tìm thấy phiên."));
        }

        logger.LogInformation("Đã thu hồi phiên {SessionId}.", sessionId);
        return Result.Success();
    }

    /// <summary>Đăng xuất tất cả thiết bị (§6.3) — phương án ứng phó khi mất máy.</summary>
    public async Task<int> RevokeAllSessionsAsync(
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var count = await store.RevokeAllSessionsAsync(exceptSessionId, cancellationToken);
        logger.LogWarning("Đã thu hồi {Count} phiên.", count);
        return count;
    }

    /// <summary>
    /// Đổi mật khẩu. §6.3: bắt nhập mật khẩu cũ, và huỷ toàn bộ phiên khác
    /// sau khi đổi.
    /// </summary>
    public async Task<Result> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        Guid? keepSessionId,
        CancellationToken cancellationToken = default)
    {
        var credential = await store.GetCredentialAsync(cancellationToken);
        if (credential is null)
        {
            return Result.Failure(ResultError.Conflict("Chưa đặt mật khẩu."));
        }

        if (passwordHasher.Verify(credential.PasswordHash, currentPassword)
            == PasswordVerificationResult.Failed)
        {
            return Result.Failure(ResultError.Unauthorized(InvalidCredentialsMessage));
        }

        var validation = ValidatePassword(newPassword);
        if (validation.IsFailure)
        {
            return validation;
        }

        credential.PasswordHash = passwordHasher.Hash(newPassword);
        credential.UpdatedAt = clock.UtcNow;
        await store.SaveCredentialAsync(credential, cancellationToken);

        // Đổi mật khẩu thường vì nghi lộ — mọi phiên khác phải chết ngay.
        var revoked = await store.RevokeAllSessionsAsync(keepSessionId, cancellationToken);

        logger.LogWarning("Đã đổi mật khẩu, thu hồi {Count} phiên khác.", revoked);
        return Result.Success();
    }

    /// <summary>
    /// Khoá tăng dần sau nhiều lần sai (§6.3). Thời gian khoá gấp đôi mỗi lần
    /// sai vượt ngưỡng, chặn trên bởi MaxLockoutDuration để không tự khoá vĩnh viễn.
    /// </summary>
    private async Task<Result> GetLockoutAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = now - _options.LockoutWindow;
        var failures = await store.CountFailedLoginsSinceAsync(since, cancellationToken);

        if (failures < _options.FailedAttemptsBeforeLockout)
        {
            return Result.Success();
        }

        var overage = failures - _options.FailedAttemptsBeforeLockout;

        // Dịch bit thay vì Math.Pow: overage lớn sẽ tràn double thành Infinity.
        // Chặn số mũ ở 16 rồi mới nhân, an toàn với mọi overage.
        var multiplier = 1L << Math.Min(overage, 16);
        var lockoutTicks = _options.BaseLockoutDuration.Ticks * multiplier;

        var lockout = lockoutTicks > _options.MaxLockoutDuration.Ticks
            ? _options.MaxLockoutDuration
            : TimeSpan.FromTicks(lockoutTicks);

        logger.LogWarning(
            "Đăng nhập bị khoá {Seconds}s sau {Failures} lần sai.",
            lockout.TotalSeconds, failures);

        return Result.Failure(ResultError.TooManyAttempts(
            $"Quá nhiều lần thử. Đợi {Math.Ceiling(lockout.TotalSeconds)} giây rồi thử lại."));
    }

    private async Task RecordFailureAsync(string? tailnetAddress, CancellationToken cancellationToken)
    {
        // §6.3/§6.5: ghi lại thời điểm và IP, KHÔNG ghi mật khẩu đã nhập.
        await store.RecordFailedLoginAsync(tailnetAddress, cancellationToken);
        logger.LogWarning("Đăng nhập thất bại từ {Address}.", tailnetAddress ?? "không rõ");
    }

    private Result ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < _options.MinimumPasswordLength)
        {
            return Result.Failure(ResultError.Validation(
                $"Mật khẩu phải dài ít nhất {_options.MinimumPasswordLength} ký tự."));
        }

        return Result.Success();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
