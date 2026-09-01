using Hub.Core.Devices;

namespace Hub.Core.Tests;

/// <summary>
/// Chọn địa chỉ MeshCentral theo lối vào.
///
/// Vì sao đáng test: tên MagicDNS chỉ phân giải được từ thiết bị trong tailnet.
/// Trả nó cho người vào qua Internet công khai thì trình duyệt báo "Không thể
/// tìm thấy địa chỉ IP của máy chủ" — đã gặp thật trên
/// <c>hub.youtubecontentgen.io.vn/remote</c>.
/// </summary>
public sealed class MeshCentralOptionsTests
{
    private const string TailnetUrl = "https://hub.tailnet-example.ts.net:4430";
    private const string PublicUrl = "https://mesh.example.com";

    [Fact]
    public void KhaiCaHai_VaoQuaTailnet_TraDiaChiTailnet()
    {
        var options = new MeshCentralOptions { Url = TailnetUrl, PublicUrl = PublicUrl };

        Assert.Equal(TailnetUrl, options.ResolveUrl(requestIsTailnet: true));
    }

    [Fact]
    public void KhaiCaHai_VaoQuaInternet_TraDiaChiCongKhai()
    {
        var options = new MeshCentralOptions { Url = TailnetUrl, PublicUrl = PublicUrl };

        Assert.Equal(PublicUrl, options.ResolveUrl(requestIsTailnet: false));
    }

    /// <summary>
    /// Đây chính là cấu hình đã gây lỗi thật: chỉ khai địa chỉ tailnet rồi mở
    /// hub qua domain công khai. Không có gì để rơi về, nên vẫn trả địa chỉ
    /// tailnet — nhưng giao diện còn nút "mở tab mới" để người dùng tự xoay xở.
    /// </summary>
    [Fact]
    public void ChiKhaiTailnet_VaoQuaInternet_RoiVeTailnet()
    {
        var options = new MeshCentralOptions { Url = TailnetUrl };

        Assert.Equal(TailnetUrl, options.ResolveUrl(requestIsTailnet: false));
    }

    [Fact]
    public void ChiKhaiCongKhai_VaoQuaTailnet_RoiVeCongKhai()
    {
        var options = new MeshCentralOptions { PublicUrl = PublicUrl };

        Assert.Equal(PublicUrl, options.ResolveUrl(requestIsTailnet: true));
    }

    [Fact]
    public void KhongKhaiGi_ChuaCauHinh()
    {
        var options = new MeshCentralOptions();

        Assert.False(options.IsConfigured);
        Assert.Null(options.ResolveUrl(requestIsTailnet: false));
    }

    /// <summary>
    /// Khai mỗi địa chỉ công khai vẫn là đã cấu hình — dùng hub thuần qua
    /// Internet là cách dùng hợp lệ, không phải thiếu sót.
    /// </summary>
    [Fact]
    public void ChiKhaiCongKhai_VanTinhLaDaCauHinh()
    {
        var options = new MeshCentralOptions { PublicUrl = PublicUrl };

        Assert.True(options.IsConfigured);
    }
}
