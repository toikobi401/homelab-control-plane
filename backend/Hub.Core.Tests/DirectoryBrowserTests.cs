using Hub.Core.Backup;

namespace Hub.Core.Tests;

/// <summary>
/// Test cho <see cref="DirectoryBrowser"/> sau khi đổi từ danh sách cho phép
/// sang danh sách chặn (2026-09-13).
///
/// Người dùng chọn được thư mục bất kỳ, nên phần còn phải giữ là **chặn đúng
/// chỗ nhạy cảm**: thư mục dữ liệu của hub chứa `hub.db` (hash mật khẩu) và
/// `appsettings.Production.json` (token Tailscale). Hệ thống đã mở ra Internet
/// (§4a) nên đây không phải rủi ro lý thuyết.
/// </summary>
public sealed class DirectoryBrowserTests : IDisposable
{
    private readonly string _base;
    private readonly string _blocked;
    private readonly string _allowed;

    public DirectoryBrowserTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "hub-browse-" + Guid.NewGuid().ToString("N")[..8]);

        _blocked = Path.Combine(_base, "du-lieu-hub");
        _allowed = Path.Combine(_base, "tai-lieu");

        Directory.CreateDirectory(Path.Combine(_blocked, "certs"));
        Directory.CreateDirectory(Path.Combine(_allowed, "anh"));
        Directory.CreateDirectory(Path.Combine(_allowed, "video"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { /* dọn dẹp */ }
    }

    /// <summary>Chặn đúng một thư mục, mọi chỗ khác duyệt được.</summary>
    private DirectoryBrowser CreateBrowser() =>
        new(new BackupOptions { BlockedPaths = [_blocked] }, dataDirectory: _blocked);

    [Fact]
    public void Khong_truyen_path_thi_tra_danh_sach_o_dia()
    {
        var result = CreateBrowser().List(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Path);
        // Duyệt được mọi ổ cố định, không còn giới hạn theo danh sách cho phép.
        Assert.NotEmpty(result.Value.Entries);
    }

    [Fact]
    public void Duyet_duoc_thu_muc_binh_thuong()
    {
        var result = CreateBrowser().List(_allowed);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.Contains(result.Value.Entries, e => e.Name == "anh");
    }

    [Fact]
    public void Thu_muc_bi_chan_thi_khong_duyet_duoc()
    {
        var result = CreateBrowser().List(_blocked);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    [Fact]
    public void Thu_muc_con_cua_vung_bi_chan_cung_bi_chan()
    {
        var result = CreateBrowser().List(Path.Combine(_blocked, "certs"));

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// `Path.GetFullPath` giải hết `..` trước khi kiểm tra, nên không vòng vào
    /// vùng bị chặn bằng đường dẫn lắt léo.
    /// </summary>
    [Fact]
    public void Khong_vong_vao_vung_bi_chan_bang_dau_cham_cham()
    {
        var traversal = Path.Combine(_allowed, "..", "du-lieu-hub");

        var result = CreateBrowser().List(traversal);

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Chặn "du-lieu-hub" không được chặn luôn "du-lieu-hub-cu": hai thư mục
    /// khác nhau, `StartsWith` trần sẽ coi cái sau nằm trong cái trước.
    /// </summary>
    [Fact]
    public void Thu_muc_cung_tien_to_ten_khong_bi_chan_lay()
    {
        var sibling = _blocked + "-cu";
        Directory.CreateDirectory(sibling);

        var result = CreateBrowser().List(sibling);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Chặn một thư mục con không có nghĩa là cấm cả thư mục cha — người dùng
    /// vẫn có thể muốn sao lưu phần còn lại của thư mục cha.
    /// </summary>
    [Fact]
    public void Thu_muc_cha_cua_vung_bi_chan_van_duyet_duoc()
    {
        var result = CreateBrowser().List(_base);

        Assert.True(result.IsSuccess);
        // Nhưng vùng bị chặn không xuất hiện trong danh sách con.
        Assert.DoesNotContain(result.Value.Entries, e => e.Name == "du-lieu-hub");
        Assert.Contains(result.Value.Entries, e => e.Name == "tai-lieu");
    }

    [Fact]
    public void Len_cha_duoc_khi_o_thu_muc_con()
    {
        var result = CreateBrowser().List(Path.Combine(_allowed, "anh"));

        Assert.True(result.IsSuccess);
        Assert.Equal(_allowed, result.Value.Parent);
    }

    [Fact]
    public void Thu_muc_khong_ton_tai_thi_bao_loi()
    {
        var result = CreateBrowser().List(Path.Combine(_allowed, "khong-co"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateSource_chan_vung_bi_chan()
    {
        var result = CreateBrowser().ValidateSource(_blocked);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateSource_chan_thu_muc_con_cua_vung_bi_chan()
    {
        var result = CreateBrowser().ValidateSource(Path.Combine(_blocked, "certs"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateSource_tra_duong_dan_da_chuan_hoa()
    {
        // Có ".." ở giữa nhưng kết quả nằm ngoài vùng chặn — hợp lệ.
        var messy = Path.Combine(_allowed, "anh", "..", "video");

        var result = CreateBrowser().ValidateSource(messy);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_allowed, "video"), result.Value);
    }

    [Fact]
    public void ValidateSource_cho_phep_thu_muc_binh_thuong()
    {
        var result = CreateBrowser().ValidateSource(_allowed);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Không khai <c>BlockedPaths</c> thì dùng mặc định: Windows, Program Files,
    /// và thư mục dữ liệu của hub truyền qua constructor.
    /// </summary>
    [Fact]
    public void Mac_dinh_chan_thu_muc_du_lieu_cua_hub()
    {
        var browser = new DirectoryBrowser(new BackupOptions(), dataDirectory: _blocked);

        Assert.True(browser.ValidateSource(_blocked).IsFailure);
        Assert.True(browser.ValidateSource(_allowed).IsSuccess);
    }

    [Fact]
    public void Mac_dinh_chan_thu_muc_Windows()
    {
        var browser = new DirectoryBrowser(new BackupOptions(), dataDirectory: _blocked);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(browser.List(windows).IsFailure);
    }

    [Fact]
    public void Danh_sach_o_dia_khong_rong()
    {
        var browser = new DirectoryBrowser(new BackupOptions(), dataDirectory: _blocked);

        Assert.NotEmpty(browser.GetRoots());
    }
}
