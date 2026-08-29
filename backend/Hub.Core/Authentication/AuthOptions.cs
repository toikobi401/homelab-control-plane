namespace Hub.Core.Authentication;

/// <summary>Tham số của xác thực (§6.3). Đọc từ cấu hình, không hardcode.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Thời hạn phiên. §6.3: mặc định 30 ngày, gia hạn trượt.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gia hạn trượt khi phiên còn dưới ngưỡng này. Không gia hạn mỗi request
    /// để đỡ ghi DB liên tục.
    /// </summary>
    public TimeSpan SlidingRenewalThreshold { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Số lần sai trước khi bắt đầu khoá (§6.3: khoá tăng dần).</summary>
    public int FailedAttemptsBeforeLockout { get; set; } = 5;

    /// <summary>Cửa sổ tính số lần sai.</summary>
    public TimeSpan LockoutWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Thời gian khoá cơ sở; tăng gấp đôi theo mỗi lần sai vượt ngưỡng.</summary>
    public TimeSpan BaseLockoutDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Trần thời gian khoá — để không tự khoá mình vĩnh viễn.</summary>
    public TimeSpan MaxLockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Độ dài tối thiểu của mật khẩu.</summary>
    public int MinimumPasswordLength { get; set; } = 12;
}
