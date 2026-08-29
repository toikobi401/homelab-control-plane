namespace Hub.Core.Results;

/// <summary>
/// Kết quả của một thao tác nghiệp vụ có thể thất bại (§7: không ném exception
/// cho luồng nghiệp vụ bình thường).
///
/// Exception vẫn dùng cho lỗi lập trình và sự cố hạ tầng — thứ mà gọi lại cũng
/// không khác gì. Còn "mật khẩu sai", "phiên hết hạn" là kết quả hợp lệ của
/// nghiệp vụ, không phải sự cố.
/// </summary>
public readonly record struct Result
{
    private Result(bool isSuccess, ResultError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public ResultError? Error { get; }

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(true, null);

    public static Result Failure(ResultError error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    public static Result<TValue> Failure<TValue>(ResultError error) => Result<TValue>.Failure(error);
}

/// <summary>Kết quả có kèm giá trị khi thành công.</summary>
public readonly record struct Result<TValue>
{
    private readonly TValue? _value;

    private Result(bool isSuccess, TValue? value, ResultError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public ResultError? Error { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Giá trị khi thành công. Đọc lúc thất bại là lỗi lập trình — ném luôn cho
    /// lộ ra ở test, thay vì trả null rồi hỏng ở chỗ khác.
    /// </summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Đọc Value của một Result thất bại ({Error?.Code}). Kiểm tra IsSuccess trước.");

    public static Result<TValue> Success(TValue value) => new(true, value, null);

    public static Result<TValue> Failure(ResultError error) => new(false, default, error);
}
