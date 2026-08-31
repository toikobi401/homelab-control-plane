// ĐÃ THAY THẾ — 2026-08-31.
//
// Năng lực 6 chuyển sang dùng MeshCentral (§2.3: tái sử dụng, đừng phát minh
// lại). Mã dưới đây là bản tự viết trước đó: nó CHẠY ĐƯỢC và có test, nhưng
// không còn là đường chính.
//
// Vì sao chuyển: MeshCentral cho sẵn Wake-on-LAN (phần khó nhất, chưa làm
// được), agent đóng gói cho mọi hệ điều hành, remote desktop, và giao diện
// mobile — đều là thứ tự làm sẽ tốn nhiều công mà không tốt hơn.
//
// Giữ lại tạm thời để còn quay về được nếu MeshCentral không hợp. Đừng xây
// thêm trên mã này; xem docs/meshcentral-setup.md.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hub.Core.Devices;
using Hub.Core.Results;

namespace Hub.Windows;

/// <summary>
/// Thực thi lệnh điều khiển nguồn trên Windows (§5a).
///
/// §3.3: mã riêng cho Windows nằm ở agent, sau interface — backend chỉ ra lệnh.
/// Ranh giới này là thứ cho phép backend chuyển sang NAS mà không viết lại.
///
/// §5a điều 3: **không gọi qua shell.** Dùng API Windows trực tiếp, hoặc
/// ProcessStartInfo với ArgumentList (mảng tham số) — không bao giờ Arguments
/// (chuỗi ghép). Không tham số nào ở đây đến từ input người dùng, và cách viết
/// này đảm bảo không có chỗ nào để nó len vào.
/// </summary>
public interface IPowerController
{
    Result Execute(PowerAction action);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerController : IPowerController
{
    public Result Execute(PowerAction action) => action switch
    {
        PowerAction.Shutdown => RunShutdownTool("/s", "/t", "0"),
        PowerAction.Restart => RunShutdownTool("/r", "/t", "0"),
        PowerAction.Sleep => Suspend(),
        PowerAction.Lock => LockWorkstation(),

        // Enum đóng, nhưng vẫn chặn tường minh: nếu ai đó thêm giá trị mới mà
        // quên hiện thực, phải thất bại rõ ràng chứ không âm thầm không làm gì.
        _ => Result.Failure(ResultError.Validation($"Hành động không hỗ trợ: {action}."))
    };

    /// <summary>
    /// Gọi shutdown.exe với ArgumentList — mỗi tham số là một phần tử mảng, hệ
    /// điều hành nhận nguyên vẹn, không qua bước phân tách chuỗi của shell.
    /// </summary>
    private static Result RunShutdownTool(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown.exe",

            // UseShellExecute = false: chạy thẳng tiến trình, KHÔNG qua shell.
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            return Result.Failure(new ResultError("power_failed", "Không khởi chạy được lệnh."));
        }

        // Chờ có giới hạn: shutdown.exe trả về ngay, nhưng không treo vô hạn nếu
        // có gì đó bất thường.
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            return Result.Failure(new ResultError("power_timeout", "Lệnh không phản hồi."));
        }

        return process.ExitCode == 0
            ? Result.Success()
            : Result.Failure(new ResultError(
                "power_failed", $"Lệnh thất bại (mã {process.ExitCode})."));
    }

    private static Result Suspend()
    {
        // hibernate=false -> sleep. disableWakeEvent=false để máy vẫn đánh thức
        // được bằng magic packet (§5a.1, phần sẽ làm sau).
        return SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false)
            ? Result.Success()
            : Result.Failure(new ResultError("power_failed", "Không chuyển được sang chế độ ngủ."));
    }

    private static Result LockWorkstation()
    {
        return NativeLockWorkStation()
            ? Result.Success()
            : Result.Failure(new ResultError("power_failed", "Không khoá được màn hình."));
    }

    // DllImport chứ không phải LibraryImport: LibraryImport sinh mã unsafe nên
    // đòi bật AllowUnsafeBlocks cho cả project. Hai hàm này chỉ nhận/trả bool,
    // không có con trỏ nào — không đáng mở unsafe cho toàn bộ Hub.Windows.
    [DllImport("powrprof.dll", EntryPoint = "SetSuspendState", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool forceCritical,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    [DllImport("user32.dll", EntryPoint = "LockWorkStation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeLockWorkStation();
}
