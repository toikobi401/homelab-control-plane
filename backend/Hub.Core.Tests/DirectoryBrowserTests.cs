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

    // ---- ResolveUnderRoot: ghép đường dẫn tương đối do trình duyệt gửi lên ----
    //
    // Trình duyệt gửi `webkitRelativePath` (dạng "Anh/2026/img.jpg") tách riêng
    // khỏi nội dung file, vì Content-Disposition chỉ mang tên trần. Đó là dữ
    // liệu client kiểm soát hoàn toàn, nên đây là bề mặt tấn công thật.

    /// <summary>Chặn đúng thư mục dữ liệu, và khai luôn vùng tải lên.</summary>
    private DirectoryBrowser CreateUploader(string uploadRoot) =>
        new(new BackupOptions { BlockedPaths = [_blocked] }, dataDirectory: _blocked, uploadRoot);

    [Fact]
    public void ResolveUnderRoot_cho_phep_thu_muc_con_nhieu_cap()
    {
        var result = CreateUploader(_allowed).ResolveUnderRoot(_allowed, "Anh/2026/img.jpg");

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_allowed, "Anh", "2026", "img.jpg"), result.Value);
    }

    [Fact]
    public void ResolveUnderRoot_chan_duong_dan_di_ra_ngoai_goc()
    {
        var result = CreateUploader(_allowed).ResolveUnderRoot(_allowed, "../du-lieu-hub/hub.db");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// `Path.Combine(root, "C:\Windows\x")` trả về "C:\Windows\x" — vứt bỏ gốc.
    /// Không chặn từ đầu thì file rơi thẳng ra ngoài mà chẳng cần ".." nào.
    /// </summary>
    [Fact]
    public void ResolveUnderRoot_chan_duong_dan_tuyet_doi()
    {
        var browser = CreateUploader(_allowed);

        Assert.True(browser.ResolveUnderRoot(_allowed, @"C:\Windows\evil.dll").IsFailure);
        Assert.True(browser.ResolveUnderRoot(_allowed, "/etc/passwd").IsFailure);
    }

    /// <summary>NTFS alternate data stream: "a.txt:hidden" ghi vào luồng ẩn.</summary>
    [Fact]
    public void ResolveUnderRoot_chan_ky_tu_hai_cham()
    {
        var result = CreateUploader(_allowed).ResolveUnderRoot(_allowed, "a.txt:hidden");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Tên thiết bị DOS mở ra thiết bị chứ không tạo file, kể cả khi có phần mở
    /// rộng — "NUL.txt" vẫn là NUL.
    /// </summary>
    [Fact]
    public void ResolveUnderRoot_chan_ten_thiet_bi_DOS()
    {
        var browser = CreateUploader(_allowed);

        Assert.True(browser.ResolveUnderRoot(_allowed, "NUL").IsFailure);
        Assert.True(browser.ResolveUnderRoot(_allowed, "nul.txt").IsFailure);
        Assert.True(browser.ResolveUnderRoot(_allowed, "Anh/COM1.jpg").IsFailure);
    }

    /// <summary>
    /// Windows lặng lẽ cắt dấu chấm và khoảng trắng ở cuối, nên "a.txt " và
    /// "a.txt" thành cùng một file — hai lần tải lên ghi đè lên nhau.
    /// </summary>
    [Fact]
    public void ResolveUnderRoot_chan_ten_ket_thuc_bang_cham_hoac_trang()
    {
        var browser = CreateUploader(_allowed);

        Assert.True(browser.ResolveUnderRoot(_allowed, "a.txt ").IsFailure);
        Assert.True(browser.ResolveUnderRoot(_allowed, "a.txt.").IsFailure);
    }

    /// <summary>
    /// "D:\HubUploads" không được nuốt "D:\HubUploads-cu" — cùng lý do với
    /// danh sách chặn, nhưng ở chiều ngược lại.
    /// </summary>
    [Fact]
    public void ResolveUnderRoot_khong_lan_sang_thu_muc_cung_tien_to()
    {
        var sibling = _allowed + "-cu";

        var result = CreateUploader(_allowed).ResolveUnderRoot(_allowed, "../tai-lieu-cu/x.txt");

        Assert.True(result.IsFailure);
        Assert.False(Directory.Exists(sibling)); // không tạo gì trên đĩa
    }

    // ---- Miễn trừ blocklist cho thư mục tải lên ----

    /// <summary>
    /// Mặc định chặn cả thư mục dữ liệu của hub. Nếu người vận hành trỏ
    /// UploadRoot vào trong đó thì không miễn trừ sẽ khiến `ValidateSource` từ
    /// chối chính thư mục hub vừa tạo, và job không bao giờ lưu được.
    /// </summary>
    [Fact]
    public void UploadRoot_khong_bi_chan_du_nam_trong_thu_muc_du_lieu()
    {
        var uploads = Path.Combine(_blocked, "uploads");
        Directory.CreateDirectory(Path.Combine(uploads, "anh-dien-thoai"));

        var browser = new DirectoryBrowser(new BackupOptions(), dataDirectory: _blocked, uploads);

        Assert.True(browser.ValidateSource(uploads).IsSuccess);
        Assert.True(browser.ValidateSource(Path.Combine(uploads, "anh-dien-thoai")).IsSuccess);
    }

    /// <summary>
    /// Miễn trừ không được nới quá tay: `hub.db` (hash mật khẩu, phiên đăng
    /// nhập) và `appsettings.Production.json` (token Tailscale) nằm cạnh thư mục
    /// tải lên nhưng NGOÀI nhánh đó, nên vẫn phải bị chặn.
    ///
    /// Đây là test quan trọng nhất của phần miễn trừ — nó là thứ phân biệt "mở
    /// đúng một nhánh" với "mở cả thư mục dữ liệu".
    /// </summary>
    [Fact]
    public void Thu_muc_du_lieu_van_bi_chan_khi_co_UploadRoot()
    {
        var uploads = Path.Combine(_blocked, "uploads");
        Directory.CreateDirectory(uploads);

        var browser = new DirectoryBrowser(new BackupOptions(), dataDirectory: _blocked, uploads);

        Assert.True(browser.ValidateSource(_blocked).IsFailure);
        Assert.True(browser.ValidateSource(Path.Combine(_blocked, "certs")).IsFailure);
        Assert.True(browser.List(_blocked).IsFailure);
    }

    /// <summary>Không khai UploadRoot thì mọi thứ chặn y như trước.</summary>
    [Fact]
    public void Khong_khai_UploadRoot_thi_khong_mien_tru_gi()
    {
        var browser = new DirectoryBrowser(new BackupOptions(), dataDirectory: _blocked);

        Assert.True(browser.ValidateSource(_blocked).IsFailure);
    }
}
