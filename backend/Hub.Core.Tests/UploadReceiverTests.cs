using System.Text;

using Hub.Core.Backup;

namespace Hub.Core.Tests;

/// <summary>
/// Test cho <see cref="UploadReceiver"/> — phần nhận file người dùng tải lên từ
/// trình duyệt (§5d).
///
/// Hai thứ đáng test nhất ở đây:
/// <list type="number">
/// <item>**Không để lại file cụt trông như file thật.** Ghi thẳng vào tên thật
/// thì request đứt giữa chừng sẽ để lại bản dở, rclone đẩy nguyên bản dở đó lên
/// cloud, và lỗi chỉ lộ ra lúc khôi phục — lúc đã muộn.</item>
/// <item>**Bỏ qua file trùng.** Đây là thứ làm cho việc chọn lại đúng thư mục cũ
/// chỉ tải phần mới thay vì tải lại tất cả.</item>
/// </list>
/// </summary>
public sealed class UploadReceiverTests : IDisposable
{
    private readonly string _root;
    private readonly UploadReceiver _receiver;
    private readonly BackupOptions _options;

    public UploadReceiverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hub-upload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        _options = new BackupOptions { UploadRoot = _root };
        var browser = new DirectoryBrowser(_options, dataDirectory: Path.Combine(_root, "..", "du-lieu"), _root);
        _receiver = new UploadReceiver(browser, _options);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* dọn dẹp */ }
    }

    private static Stream Content(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private string Folder(string name)
    {
        var prepared = _receiver.PrepareFolder(name);
        Assert.True(prepared.IsSuccess);
        return prepared.Value;
    }

    [Fact]
    public async Task Nhan_file_thi_ghi_dung_noi_dung_va_cau_truc_thu_muc()
    {
        var folder = Folder("tai-lieu");

        var outcome = await _receiver.ReceiveFileAsync(
            folder, "Anh/2026/img.jpg", Content("noi dung anh"), CancellationToken.None);

        Assert.Equal(UploadFileOutcome.Received, outcome);

        var written = Path.Combine(folder, "Anh", "2026", "img.jpg");
        Assert.True(File.Exists(written));
        Assert.Equal("noi dung anh", await File.ReadAllTextAsync(written, CancellationToken.None));
    }

    [Fact]
    public async Task File_trung_noi_dung_thi_bo_qua_va_dem_vao_duplicates()
    {
        var folder = Folder("tai-lieu");
        var ct = CancellationToken.None;

        var first = await _receiver.ReceiveFileAsync(folder, "ghi-chu.txt", Content("y het"), ct);
        var second = await _receiver.ReceiveFileAsync(folder, "ghi-chu.txt", Content("y het"), ct);

        Assert.Equal(UploadFileOutcome.Received, first);
        Assert.Equal(UploadFileOutcome.Duplicate, second);
    }

    /// <summary>
    /// Cùng tên nhưng nội dung đã đổi thì phải ghi đè — coi là trùng ở đây nghĩa
    /// là bản sao lưu vĩnh viễn giữ nội dung cũ.
    /// </summary>
    [Fact]
    public async Task File_cung_ten_khac_noi_dung_thi_ghi_de()
    {
        var folder = Folder("tai-lieu");
        var ct = CancellationToken.None;

        await _receiver.ReceiveFileAsync(folder, "ghi-chu.txt", Content("ban cu"), ct);
        var outcome = await _receiver.ReceiveFileAsync(folder, "ghi-chu.txt", Content("ban moi"), ct);

        Assert.Equal(UploadFileOutcome.Received, outcome);
        Assert.Equal("ban moi", await File.ReadAllTextAsync(Path.Combine(folder, "ghi-chu.txt"), ct));
    }

    /// <summary>
    /// Cùng kích thước nhưng khác nội dung: nếu chỉ so cỡ file mà bỏ hash thì
    /// test này bắt được — và đó đúng là kiểu hỏng âm thầm nhất.
    /// </summary>
    [Fact]
    public async Task Cung_kich_thuoc_khac_noi_dung_van_ghi_de()
    {
        var folder = Folder("tai-lieu");
        var ct = CancellationToken.None;

        await _receiver.ReceiveFileAsync(folder, "a.txt", Content("AAAA"), ct);
        var outcome = await _receiver.ReceiveFileAsync(folder, "a.txt", Content("BBBB"), ct);

        Assert.Equal(UploadFileOutcome.Received, outcome);
        Assert.Equal("BBBB", await File.ReadAllTextAsync(Path.Combine(folder, "a.txt"), ct));
    }

    [Fact]
    public async Task Duong_dan_di_ra_ngoai_thu_muc_bi_tu_choi()
    {
        var folder = Folder("tai-lieu");

        var outcome = await _receiver.ReceiveFileAsync(
            folder, "../../hub.db", Content("trom du lieu"), CancellationToken.None);

        Assert.Equal(UploadFileOutcome.Rejected, outcome);
        // Và không có gì rơi ra ngoài thư mục tải lên.
        Assert.False(File.Exists(Path.Combine(_root, "..", "hub.db")));
    }

    /// <summary>
    /// Giới hạn hiện kiểm SAU khi ghi xong, nên nó chặn việc GIỮ file quá lớn
    /// chứ không chặn việc nhận. Chặn theo thời gian/băng thông là việc của
    /// Kestrel:Limits:MaxRequestBodySize ở tầng HTTP — ghi lại ở đây để lần sau
    /// đọc code không tưởng tầng này đã lo hết.
    /// </summary>
    [Fact]
    public async Task File_qua_lon_bi_tu_choi_va_khong_de_lai_rac()
    {
        _options.Upload.MaxFileBytes = 4;
        var folder = Folder("tai-lieu");

        var outcome = await _receiver.ReceiveFileAsync(
            folder, "to.bin", Content("dai hon bon byte"), CancellationToken.None);

        Assert.Equal(UploadFileOutcome.Rejected, outcome);
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>
    /// Huỷ giữa lúc ghi: không được để lại file mang tên thật, vì rclone sẽ đẩy
    /// bản cụt đó lên cloud như một bản sao lưu hợp lệ.
    /// </summary>
    [Fact]
    public async Task Ghi_dut_giua_chung_khong_de_lai_file_hoan_chinh_gia()
    {
        var folder = Folder("tai-lieu");
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _receiver.ReceiveFileAsync(folder, "video.mp4", new CancellingStream(cts), cts.Token));

        Assert.False(File.Exists(Path.Combine(folder, "video.mp4")));
        Assert.Empty(Directory.GetFiles(folder, "*" + UploadReceiver.PartialSuffix));
    }

    [Fact]
    public async Task Don_duoc_file_part_mo_coi()
    {
        var folder = Folder("tai-lieu");
        var orphan = Path.Combine(folder, "dang-do.jpg" + UploadReceiver.PartialSuffix);
        await File.WriteAllTextAsync(orphan, "ghi do", CancellationToken.None);

        var removed = _receiver.CleanPartialFiles();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void Ten_thu_muc_di_ra_ngoai_bi_tu_choi()
    {
        Assert.True(_receiver.PrepareFolder("../ngoai").IsFailure);
        Assert.True(_receiver.PrepareFolder(@"C:\Windows").IsFailure);
    }

    /// <summary>Stream huỷ token ngay giữa lần đọc thứ hai, giả lập đứt kết nối.</summary>
    private sealed class CancellingStream(CancellationTokenSource cts) : Stream
    {
        private int _reads;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ > 0)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }

            buffer[offset] = 1;
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_reads++ > 0)
            {
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
