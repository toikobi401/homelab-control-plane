using Hub.Core.Backup;

namespace Hub.Core.Tests;

/// <summary>
/// Test cho <see cref="DirectoryBrowser"/> — bề mặt tấn công mới của năng lực 3.
///
/// Hệ thống đã mở ra Internet (§4a), nên endpoint liệt kê thư mục là thứ ai
/// chiếm được phiên đăng nhập cũng gọi được. Path traversal ở đây không phải
/// rủi ro lý thuyết: `../../../Windows/System32` phải bị chặn, và test phải
/// chứng minh điều đó chứ không tin vào việc đã gọi `Path.GetFullPath`.
/// </summary>
public sealed class DirectoryBrowserTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;

    public DirectoryBrowserTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "hub-browse-" + Guid.NewGuid().ToString("N")[..8]);

        _root = Path.Combine(baseDir, "cho-phep");
        _outside = Path.Combine(baseDir, "ngoai-pham-vi");

        Directory.CreateDirectory(Path.Combine(_root, "con-a"));
        Directory.CreateDirectory(Path.Combine(_root, "con-b"));
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        try
        {
            var parent = Path.GetDirectoryName(_root);
            if (parent is not null) { Directory.Delete(parent, recursive: true); }
        }
        catch { /* dọn dẹp, lỗi không quan trọng */ }
    }

    private DirectoryBrowser CreateBrowser() =>
        new(new BackupOptions { BrowseRoots = [_root] });

    [Fact]
    public void Khong_truyen_path_thi_tra_danh_sach_goc()
    {
        var result = CreateBrowser().List(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Path);
        Assert.Single(result.Value.Entries);
        Assert.Equal(_root, result.Value.Entries[0].Path);
    }

    [Fact]
    public void Liet_ke_thu_muc_con_trong_pham_vi()
    {
        var result = CreateBrowser().List(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.Contains(result.Value.Entries, e => e.Name == "con-a");
        Assert.Contains(result.Value.Entries, e => e.Name == "con-b");
    }

    /// <summary>
    /// Lỗi kinh điển của mọi "file server tự viết" (§5c). Chuỗi `..` phải bị
    /// chặn sau khi chuẩn hoá, không phải bị lọc bằng cách tìm chuỗi con.
    /// </summary>
    [Fact]
    public void Path_traversal_bi_chan()
    {
        var traversal = Path.Combine(_root, "..", "ngoai-pham-vi");

        var result = CreateBrowser().List(traversal);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Value.Code);
    }

    [Fact]
    public void Duong_dan_tuyet_doi_ngoai_pham_vi_bi_chan()
    {
        var result = CreateBrowser().List(_outside);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Duong_dan_he_thong_bi_chan()
    {
        var result = CreateBrowser().List(@"C:\Windows\System32");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// "D:\App" và "D:\AppData" là hai thư mục khác nhau. So sánh bằng
    /// StartsWith trần sẽ coi cái sau nằm trong cái trước — phải so kèm dấu
    /// phân cách.
    /// </summary>
    [Fact]
    public void Thu_muc_cung_tien_to_ten_khong_bi_coi_la_ben_trong()
    {
        var sibling = _root + "-them";
        Directory.CreateDirectory(sibling);

        try
        {
            var result = CreateBrowser().List(sibling);
            Assert.True(result.IsFailure);
        }
        finally
        {
            try { Directory.Delete(sibling, recursive: true); } catch { /* dọn dẹp */ }
        }
    }

    [Fact]
    public void Len_cha_khong_di_ra_ngoai_goc()
    {
        // Đứng ở chính gốc thì không có cha nào hợp lệ để lên.
        var result = CreateBrowser().List(_root);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Parent);
    }

    [Fact]
    public void Len_cha_trong_pham_vi_thi_duoc()
    {
        var child = Path.Combine(_root, "con-a");

        var result = CreateBrowser().List(child);

        Assert.True(result.IsSuccess);
        Assert.Equal(_root, result.Value.Parent);
    }

    [Fact]
    public void Thu_muc_khong_ton_tai_thi_bao_loi()
    {
        var result = CreateBrowser().List(Path.Combine(_root, "khong-co"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateSource_chan_duong_dan_ngoai_pham_vi()
    {
        var result = CreateBrowser().ValidateSource(_outside);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void ValidateSource_tra_duong_dan_da_chuan_hoa()
    {
        // Đường dẫn có ".." ở giữa nhưng kết quả vẫn nằm trong gốc — hợp lệ.
        var messy = Path.Combine(_root, "con-a", "..", "con-b");

        var result = CreateBrowser().ValidateSource(messy);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_root, "con-b"), result.Value);
    }

    /// <summary>
    /// Không khai gốc nào thì rơi về mọi ổ cố định — tiện lúc bắt đầu, nhưng
    /// phải thật sự trả về thứ gì đó, không phải danh sách rỗng.
    /// </summary>
    [Fact]
    public void Khong_khai_BrowseRoots_thi_dung_o_dia_co_dinh()
    {
        var browser = new DirectoryBrowser(new BackupOptions());

        var roots = browser.GetRoots();

        Assert.NotEmpty(roots);
    }
}
