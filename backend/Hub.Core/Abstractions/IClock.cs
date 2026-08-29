namespace Hub.Core.Abstractions;

/// <summary>
/// Nguồn thời gian. Có interface này để test được logic hết hạn phiên và khoá
/// đăng nhập mà không phải chờ thật.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
