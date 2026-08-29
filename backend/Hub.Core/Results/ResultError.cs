namespace Hub.Core.Results;

/// <summary>
/// Lỗi nghiệp vụ. <paramref name="Code"/> để code xử lý, <paramref name="Message"/>
/// để hiện cho người dùng.
///
/// §6.5 mục 7: không hiện chi tiết lỗi ra frontend. Message ở đây phải là câu
/// chung chung an toàn — chi tiết đi vào log.
/// </summary>
public readonly record struct ResultError(string Code, string Message)
{
    public static ResultError Unauthorized(string message) => new("unauthorized", message);

    public static ResultError Validation(string message) => new("validation", message);

    public static ResultError Conflict(string message) => new("conflict", message);

    public static ResultError TooManyAttempts(string message) => new("too_many_attempts", message);
}
