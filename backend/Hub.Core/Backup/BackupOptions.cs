namespace Hub.Core.Backup;

/// <summary>
/// Cấu hình sao lưu — năng lực 3.
///
/// §2.3: gọi binary <c>rclone</c> thay vì tự viết client Google Drive. Rclone đã
/// giải xong những thứ khó của việc đồng bộ file lên cloud: kiểm tra hash, thử
/// lại khi lỗi mạng, truyền song song, giới hạn băng thông.
/// </summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>
    /// Đường dẫn tới <c>rclone.exe</c>. Để trống thì tìm trong PATH.
    ///
    /// Khai tường minh khi chạy như Windows Service: service chạy dưới
    /// LocalSystem nên PATH khác của người dùng — xem docs/services.md.
    /// </summary>
    public string RclonePath { get; set; } = "rclone";

    /// <summary>
    /// File cấu hình rclone (<c>rclone.conf</c>). Để trống thì rclone tự tìm
    /// trong thư mục hồ sơ người dùng.
    ///
    /// Cùng lý do với <see cref="RclonePath"/>: service không thấy hồ sơ của
    /// bạn, nên phải khai đường dẫn tuyệt đối.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Các công việc sao lưu. Mỗi mục là một cặp "thư mục nguồn → đích trên cloud".
    /// </summary>
    public List<BackupJobOptions> Jobs { get; set; } = [];

    /// <summary>
    /// Các thư mục KHÔNG được duyệt hay chọn làm nguồn sao lưu.
    ///
    /// Đổi từ danh sách cho phép sang danh sách chặn (2026-09-13): người dùng
    /// cần chọn thư mục bất kỳ, nên duyệt được mọi ổ đĩa cố định. Chỉ chặn vài
    /// chỗ vừa không ai sao lưu, vừa lộ nhiều nhất nếu phiên đăng nhập bị chiếm.
    ///
    /// Để trống thì dùng mặc định của <see cref="DirectoryBrowser"/>: thư mục
    /// Windows, Program Files, và thư mục dữ liệu của hub (chứa
    /// <c>hub.db</c> và <c>appsettings.Production.json</c> với token Tailscale).
    ///
    /// Đây là nới lỏng có ý thức, không phải sơ suất — hệ thống đã mở ra
    /// Internet (§4a) nên endpoint duyệt thư mục là bề mặt tấn công thật.
    /// </summary>
    public List<string> BlockedPaths { get; set; } = [];

    /// <summary>
    /// Số bản sao lưu gần nhất giữ trong lịch sử. Cũ hơn thì xoá khỏi DB —
    /// không xoá file trên cloud.
    /// </summary>
    public int HistoryLimit { get; set; } = 50;

    /// <summary>
    /// Giới hạn thời gian một lần chạy, tính bằng phút. Quá hạn thì huỷ tiến
    /// trình rclone.
    ///
    /// Không để vô hạn: một lần sync treo sẽ giữ khoá và chặn mọi lần chạy sau,
    /// mà nhìn từ giao diện thì chỉ thấy "đang chạy" mãi mãi.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 120;

    public bool IsConfigured => Jobs.Count > 0;
}

/// <summary>Một công việc sao lưu.</summary>
public sealed class BackupJobOptions
{
    /// <summary>Tên hiển thị, cũng là định danh trong API. Ví dụ "tai-lieu".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Thư mục nguồn trên máy này.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Đích trên rclone, dạng <c>remote:đường/dẫn</c>.
    ///
    /// Trỏ vào remote <c>crypt</c> thì file trên cloud là ciphertext — bắt buộc
    /// với dữ liệu nhạy cảm, xem <see cref="Encrypted"/>.
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Đích này có phải remote đã mã hoá không. Chỉ dùng để hiển thị và kiểm
    /// tra — rclone tự biết qua cấu hình remote.
    ///
    /// Vì sao cần: người dùng chọn lưu nguyên bản để xem/tải trực tiếp từ web
    /// Drive, NHƯNG hub.db chứa hash mật khẩu và session nên phải mã hoá riêng.
    /// Cờ này để giao diện nói rõ file nào được bảo vệ, và để cảnh báo nếu ai
    /// đó vô tình khai hub.db vào một đích không mã hoá.
    /// </summary>
    public bool Encrypted { get; set; }

    /// <summary>
    /// Xoá file ở đích khi nguồn không còn (<c>rclone sync</c>) hay chỉ thêm
    /// (<c>rclone copy</c>).
    ///
    /// Mặc định <c>false</c> — an toàn hơn. Bật <c>sync</c> nghĩa là xoá nhầm
    /// ở máy sẽ lan lên cloud, và bản sao lưu mất luôn giá trị cứu hộ.
    /// </summary>
    public bool DeleteExtra { get; set; }

    /// <summary>
    /// Đường dẫn file filter kiểu rclone (cú pháp gần .gitignore), truyền qua
    /// <c>--filter-from</c>. Để trống thì sao lưu toàn bộ <see cref="Source"/>,
    /// không lọc gì.
    ///
    /// Không đọc hay parse nội dung file ở phía .NET — rclone tự đọc khi được
    /// gọi. Viết lại parser là đúng thứ §2.3 cấm.
    /// </summary>
    public string? FilterFile { get; set; }

    /// <summary>Bỏ qua job này mà không cần xoá cấu hình.</summary>
    public bool Enabled { get; set; } = true;
}
