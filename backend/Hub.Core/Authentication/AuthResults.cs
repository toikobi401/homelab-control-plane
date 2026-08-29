namespace Hub.Core.Authentication;

/// <summary>Kết quả đăng nhập thành công: phiên vừa tạo.</summary>
public sealed record LoginSuccess(Session Session);

/// <summary>Trạng thái khởi tạo của hệ thống, cho frontend biết cần hiện màn hình nào.</summary>
public sealed record AuthStatus(bool PasswordConfigured);
