namespace Hub.Core.Backup;

/// <summary>
/// Khoá theo tên job, dùng chung cho toàn ứng dụng.
///
/// Vì sao tách khỏi <see cref="BackupService"/>: service là scoped (nó cần
/// DbContext, mà DbContext là scoped), nên mỗi request tạo một instance mới.
/// Khoá nằm trong instance sẽ vô tác dụng — hai request đồng thời mỗi cái giữ
/// một khoá riêng và cùng chạy rclone lên một đích.
///
/// Lớp này đăng ký singleton để mọi request thấy chung một bộ khoá.
/// </summary>
public sealed class BackupJobLocks
{
    private readonly Dictionary<string, SemaphoreSlim> _locks = [];
    private readonly Lock _guard = new();

    public SemaphoreSlim Get(string jobName)
    {
        lock (_guard)
        {
            var key = Normalize(jobName);
            if (!_locks.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _locks[key] = gate;
            }
            return gate;
        }
    }

    public bool IsHeld(string jobName)
    {
        lock (_guard)
        {
            return _locks.TryGetValue(Normalize(jobName), out var gate)
                && gate.CurrentCount == 0;
        }
    }

    private static string Normalize(string jobName) => jobName.ToLowerInvariant();
}
