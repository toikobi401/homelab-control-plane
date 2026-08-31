# Cấu hình Tailscale API

Backend đọc danh sách thiết bị trong tailnet qua **Tailscale API v2** (năng lực 1 — hiện diện).
Tài liệu này nói cách lấy thông tin xác thực và đặt nó vào đâu.

## Tại sao không dùng tài khoản GitHub trực tiếp

Bạn đăng nhập Tailscale bằng GitHub, nhưng **API không nhận tài khoản GitHub**. GitHub chỉ là nhà
cung cấp danh tính để bạn vào được admin console; còn để gọi API thì phải tạo riêng một **OAuth
client** trong console đó. Đây là thiết kế của Tailscale, không phải hạn chế của hệ thống này.

## Lấy OAuth client

1. Mở <https://console.tailscale.com/admin/settings/trust-credentials> (đăng nhập bằng GitHub như
   thường lệ). Trang này trước đây tên là "OAuth clients"; nếu link cũ
   `login.tailscale.com/admin/settings/oauth` còn chạy thì nó cũng dẫn về đây.
2. Bấm **Credential** → chọn **OAuth**.
3. Ở scope **`devices:core`**, tick quyền **Read**. Đừng tick Write, và đừng cấp scope nào khác —
   hệ thống chỉ đọc danh sách thiết bị, không sửa gì trong tailnet.
4. Bấm **Generate credential**.
5. **Chép ngay cả client ID lẫn client secret**, rồi mới bấm **Done**. Secret chỉ hiện đúng một
   lần; đóng trang là mất, phải tạo cái mới. Secret phân biệt hoa thường.

## Đặt vào đâu

§6.5 mục 1: **không secret trong source.** Không viết thẳng vào `appsettings.json` — file đó được
commit.

### Lúc phát triển — .NET User Secrets

Chạy ở thư mục gốc repo:

```bash
dotnet user-secrets --project backend/Hub.Api set "Tailscale:ClientId" "<client-id>"
dotnet user-secrets --project backend/Hub.Api set "Tailscale:ClientSecret" "<client-secret>"
```

User Secrets lưu ngoài repo (`%APPDATA%\Microsoft\UserSecrets` trên Windows), nên không có đường nào
lọt vào git.

### Lúc chạy thật — biến môi trường

```yaml
# compose.yaml
environment:
  Tailscale__ClientId: ${TAILSCALE_CLIENT_ID}
  Tailscale__ClientSecret: ${TAILSCALE_CLIENT_SECRET}
```

Hai dấu gạch dưới `__` là cách .NET biểu diễn cấu hình lồng nhau qua biến môi trường. Giá trị thật
đặt trong `.env` — file này đã bị `.gitignore` chặn.

## Kiểm tra

```bash
curl -k https://localhost:7189/api/devices -b cookies.txt
```

Cần đăng nhập trước (§6.4: tailnet không phải lớp phòng thủ duy nhất).

| Mã trả về | Nghĩa |
|---|---|
| `200` | Chạy đúng |
| `401` | Chưa đăng nhập vào hub |
| `503` | Chưa khai `ClientId`/`ClientSecret` |
| `502` | Khai rồi nhưng sai, hoặc Tailscale đang không gọi được |

## Hai điều cần biết về dữ liệu trả về

**1. Trạng thái online là suy luận, không phải sự thật.** API danh sách thiết bị của Tailscale
**không có trường `online`** — chỉ có `lastSeen`. Hệ thống suy ra bằng ngưỡng
`Tailscale:OnlineThreshold` (mặc định 5 phút). Vì vậy giao diện nên hiện "thấy lần cuối X phút
trước" thay vì khẳng định chắc chắn máy đang bật.

**2. Đây chưa phải sổ đăng ký thiết bị đầy đủ của §5a.** Tailscale không biết địa chỉ MAC, nhãn LAN,
hay khả năng đánh thức — những thứ đó chỉ agent chạy trên máy mới báo được. Khi làm năng lực 6, sổ
đăng ký sẽ hợp nhất hai nguồn chứ không thay thế nguồn này.

## Tuỳ chọn khác

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `Tailscale:Tailnet` | `-` | `-` nghĩa là tailnet mặc định của credential. Đủ cho hệ thống một người dùng |
| `Tailscale:OnlineThreshold` | `00:05:00` | Quá ngưỡng này kể từ `lastSeen` thì coi là offline |
| `Tailscale:CacheDuration` | `00:00:30` | Giữ cache bao lâu, để không đụng giới hạn tần suất của Tailscale |
