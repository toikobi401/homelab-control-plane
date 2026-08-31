# Mở hub ra Internet qua Cloudflare Tunnel

Cho phép vào hub từ mọi thiết bị, không cần cài Tailscale.

⚠️ **Đọc CONTEXT.md §4a trước.** Đây là ngoại lệ có điều kiện với ràng buộc gốc của dự án, kèm những
thứ bắt buộc phải có. Hệ thống này tắt máy và điều khiển màn hình từ xa — phơi nó ra Internet không
phải quyết định nhỏ.

## Tạm thời: port forwarding (chưa có tên miền)

Cách nhanh khi chưa mua miền. **Kém an toàn hơn Cloudflare Tunnel** — không có lớp chống DDoS/WAF,
và IP nhà lộ ra. Chuyển sang tunnel ngay khi có miền.

### Bước 1 — chạy hub ở chế độ LAN

```powershell
$env:HUB_BIND_MODE = "Lan"
cd backend/Hub.Api
dotnet run
```

Nó bind vào **card mạng vật lý** (`192.168.1.10`), không phải `0.0.0.0` — Radmin VPN và các card
ảo không lộ theo.

### Bước 2 — mở firewall cho LAN

PowerShell **quyền admin**:

```powershell
New-NetFirewallRule -DisplayName "Hub.Api (LAN 7189)" `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7189 `
  -LocalAddress 192.168.1.10
```

### Bước 3 — cấu hình router

Vào `http://192.168.0.1`, tìm mục **Port Forwarding** / **Virtual Server**:

| Trường | Giá trị |
|---|---|
| Cổng ngoài | `8443` |
| IP nội bộ | `192.168.1.10` |
| Cổng nội bộ | `7189` |
| Giao thức | TCP |

Cổng ngoài **8443** thay vì 443: bot quét tự động chủ yếu nhắm cổng phổ biến.

### Bước 4 — kiểm chứng từ ngoài

Từ điện thoại dùng **4G** (không phải Wi-Fi nhà):

```
https://<ip-cong-cong-cua-ban>:8443
```

Trình duyệt cảnh báo chứng chỉ — chứng chỉ dev chỉ hợp lệ cho `localhost`. Bấm **Advanced →
Proceed**.

### Ba điều phải biết

**1. IP nhà thay đổi.** Nhà mạng VN thường cấp IP động — hôm nay `<ip-cong-cong-cua-ban>`, mai có thể khác.
Cần DDNS (nhiều router có sẵn) hoặc kiểm tra lại mỗi lần.

**2. Không có chống DDoS.** Ai biết IP và cổng đều dội request được. Rate limit là phòng thủ duy
nhất — nó chặn ở request thứ 11 mỗi phút cho endpoint đăng nhập.

**3. Nên đổi sang tunnel sớm.** Một tên miền vài đô một năm đổi được: chứng chỉ thật (không cảnh
báo, dùng được trên iPhone), chống DDoS, WAF, và **không mở cổng nào trên router**.

---

## Vì sao Cloudflare Tunnel, không phải port forwarding

| | Port forwarding | Cloudflare Tunnel |
|---|---|---|
| Mở cổng trên router | Có | **Không** |
| IP nhà lộ ra | Có | Không |
| Chống DDoS / WAF | Không | Có |
| Chứng chỉ HTTPS | Tự lo | Tự động |
| Bên thứ ba thấy lưu lượng | Không | **Có** — xem §4a |

Điểm cuối là cái giá thật: TLS kết thúc ở biên Cloudflare, họ thấy được lưu lượng đã giải mã.

## Chuẩn bị

Cần một **tên miền** đã trỏ nameserver về Cloudflare. Tên miền rẻ nhất cũng được — không cần đẹp.

## Bước 1 — cài cloudflared

```powershell
winget install --id Cloudflare.cloudflared
```

## Bước 2 — đăng nhập và tạo tunnel

```powershell
& "C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel login
& "C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel create hub
```

Lệnh `create` in ra **Tunnel ID** và đường dẫn file credentials — giữ lại.

## Bước 3 — cấu hình

Tạo `%USERPROFILE%\.cloudflared\config.yml`:

```yaml
tunnel: <tunnel-id>
credentials-file: C:\Users\<tên>\.cloudflared\<tunnel-id>.json

ingress:
  # Hub — cổng loopback của chế độ Tunnel
  - hostname: hub.tenmien-cua-ban.com
    service: http://127.0.0.1:7190

  # MeshCentral — chứng chỉ tự ký nên phải bỏ qua kiểm tra
  - hostname: mesh.tenmien-cua-ban.com
    service: https://100.100.100.100:4430
    originRequest:
      noTLSVerify: true

  # Bắt buộc có, nếu không cloudflared từ chối khởi động
  - service: http_status:404
```

## Bước 4 — trỏ DNS

```powershell
& "C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel route dns hub hub.tenmien-cua-ban.com
& "C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel route dns hub mesh.tenmien-cua-ban.com
```

## Bước 5 — chạy hub ở chế độ Tunnel

```powershell
$env:HUB_BIND_MODE = "Tunnel"
cd backend/Hub.Api
dotnet run
```

Chế độ này chỉ nghe `127.0.0.1:7190` — **không cổng nào mở ra LAN hay Internet**, kể cả khi firewall
bị tắt nhầm. Chỉ `cloudflared` trên chính máy đó gọi vào được.

## Bước 6 — chạy tunnel

```powershell
& "C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel run hub
```

Chạy thường trực thì cài làm service (PowerShell **quyền admin**):

```powershell
& "C:\Program Files (x86)\cloudflared\cloudflared.exe" service install
```

## Kiểm chứng

```bash
curl https://hub.tenmien-cua-ban.com/health          # phải 200, không cần -k
curl -I https://hub.tenmien-cua-ban.com/ | grep -i strict-transport   # phải có HSTS
```

Thử dò mật khẩu — request thứ 11 trong một phút phải trả **429**:

```bash
for i in $(seq 1 12); do
  curl -s -o /dev/null -w "%{http_code} " -X POST https://hub.tenmien-cua-ban.com/api/auth/login \
    -H "Content-Type: application/json" -d '{"password":"sai"}'
done
```

## Bẫy: hub sau tunnel không biết mình đang chạy HTTPS

Cloudflare kết thúc TLS ở biên rồi gọi hub bằng **HTTP** trên loopback. Hub thấy
`Request.IsHttps == false`, và hai thứ hỏng theo:

- Cookie antiforgery đặt `SecurePolicy = Always` nên ASP.NET từ chối ghi nó, ném
  `InvalidOperationException` → **mọi POST trả 500**, kể cả đăng nhập.
- Header **HSTS không được đặt** vì ta chỉ đặt nó trên kết nối HTTPS.

Đã gặp thật khi dựng tunnel: đăng nhập trả 500 và thiếu HSTS.

Sửa bằng `UseForwardedHeaders()` để hub đọc `X-Forwarded-Proto`. Nhưng đó là header **client tự đặt
được** — tin nó bừa nghĩa là ai cũng giả được "tôi đang dùng HTTPS", vô hiệu hoá luôn cờ Secure
trên cookie.

Vì vậy `ForwardedHeadersSetup` **chỉ bật ở chế độ Tunnel**, nơi hub chỉ nghe loopback: request duy
nhất tới được là từ `cloudflared` trên chính máy đó.

Kiểm chứng sau khi sửa:

```bash
curl -I https://hub.tenmien-cua-ban.com/health | grep -i strict-transport
# phải có: strict-transport-security: max-age=31536000

curl -o /dev/null -w "%{http_code}" -X POST https://hub.tenmien-cua-ban.com/api/auth/login   -H "Content-Type: application/json" -d '{"password":"sai"}'
# phải 400 hoặc 401, KHÔNG phải 500
```

## Nên làm thêm: Cloudflare Access

Cloudflare Access đặt một lớp đăng nhập **trước khi** request chạm tới hub. Bot quét cổng không bao
giờ thấy được màn hình đăng nhập của ta.

Zero Trust → Access → Applications → Add → Self-hosted → chọn `hub.tenmien-cua-ban.com`, rồi thêm policy
giới hạn theo email của bạn.

Miễn phí tới 50 người dùng. Với hệ thống có quyền tắt máy từ xa thì đây là lớp đáng có.

## Ba điều cần biết

**Tailnet vẫn dùng được và nên ưu tiên.** Ở nhà thì `https://hub.tailnet-example.ts.net:7189`
nhanh hơn và không qua bên thứ ba. Cloudflare Tunnel là đường cho lúc ở ngoài.

**MeshCentral cần thêm origin.** Sau khi có tên miền, thêm vào
`D:\App\MeshCentral\meshcentral-data\config.json`:

```json
"allowedFramingOrigins": ["https://hub.tenmien-cua-ban.com", ...],
"allowedorigin": ["mesh.tenmien-cua-ban.com", ...]
```

Thiếu thì iframe trong tab Điều khiển máy hiện trắng.

**Rate limit phân vùng theo `CF-Connecting-IP`.** Header này do chính Cloudflare đặt và ghi đè giá
trị client gửi lên. Không dùng `X-Forwarded-For` — client tự đặt được, đổi header mỗi request là
thoát rate limit hoàn toàn.
