# Cài agent để điều khiển máy từ xa

Agent (`Hub.Agent`) chạy trên mỗi máy desktop muốn điều khiển từ xa. Nó làm hai việc:

1. **Báo danh** với hub — gửi tên máy, MAC, nhãn LAN (những thứ Tailscale không biết)
2. **Nhận lệnh** điều khiển nguồn: shutdown / restart / sleep / lock

Backend **không** tự thực thi lệnh Windows — nó chỉ ra lệnh cho agent (§3.3). Ranh giới này là thứ
cho phép backend chuyển sang NAS sau này mà không phải viết lại.

## Khoá chung giữa hub và agent

§6.4: tailnet là lớp phòng thủ thứ nhất, **không phải duy nhất**. Không có khoá chung thì bất kỳ
thiết bị nào trong tailnet cũng gọi thẳng agent và tắt máy được.

Sinh một khoá ngẫu nhiên (chạy một lần, dùng chung cho mọi máy):

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))
```

### Trên máy chạy hub

```bash
dotnet user-secrets --project backend/Hub.Api set "Agent:SharedSecret" "<khoá>"
```

### Trên mỗi máy chạy agent

```bash
dotnet user-secrets --project backend/Hub.Agent set "Agent:SharedSecret" "<khoá>"
dotnet user-secrets --project backend/Hub.Agent set "Agent:HubUrl" "https://<ip-tailnet-của-hub>:7189"
```

Máy **đang chạy hub** khai thêm:

```bash
dotnet user-secrets --project backend/Hub.Agent set "Agent:IsBackendHost" "true"
```

Khai sai chỗ này thì §5a điều 5 mất tác dụng — hub sẽ cho phép tự tắt chính nó.

§6.5 mục 1: không viết khoá vào `appsettings.json` — file đó được commit. Lúc chạy thật dùng biến
môi trường `Agent__SharedSecret`.

## Duyệt thiết bị

§5a: thiết bị mới **phải được duyệt thủ công** một lần trước khi nhận lệnh. Agent đăng ký xong là ở
trạng thái chờ duyệt.

```
GET  /api/devices/registered          xem danh sách, kèm isApproved
POST /api/devices/{id}/approve        duyệt
POST /api/devices/{id}/revoke-approval  thu hồi khi nghi ngờ
```

Đăng ký lại **không** tự nâng quyền — một agent bị chiếm không thể gọi `/register` để thoát khỏi
trạng thái chờ duyệt.

## Các lệnh

§5a điều 1: **mỗi hành động một endpoint riêng.** Không có endpoint nhận tham số `action` rồi rẽ
nhánh, và không bao giờ nhận chuỗi lệnh.

```
POST /api/devices/{id}/shutdown
POST /api/devices/{id}/restart
POST /api/devices/{id}/sleep
POST /api/devices/{id}/lock
GET  /api/devices/commands       nhật ký kiểm toán
```

Tất cả đều yêu cầu đăng nhập hub.

| Mã | Nghĩa |
|---|---|
| `204` | Đã gửi lệnh tới agent |
| `400` | Thiết bị chưa được duyệt, hoặc không tồn tại |
| `409` | Máy đích đang chạy hub — không tắt được (§5a điều 5) |
| `502` | Không liên lạc được agent (máy tắt, agent chưa chạy) |
| `503` | Chưa cấu hình `Agent:SharedSecret` |
| `504` | Agent không phản hồi trong thời gian chờ |

## Nhật ký kiểm toán

§5a điều 7: **mọi** lệnh đều được ghi — thời điểm, phiên nào gọi, máy đích, hành động, kết quả.
Lệnh bị **từ chối cũng được ghi**: một chuỗi lệnh bị chặn là dấu hiệu đáng chú ý.

Đây là ngoại lệ có chủ đích với quy tắc "không log" ở §6.5 — hệ thống có quyền tắt máy từ xa thì
phải trả lời được câu "ai đã tắt máy tôi lúc 3 giờ sáng".

## Chạy agent như dịch vụ

Lúc phát triển, chạy thẳng trong terminal:

```bash
dotnet run --project backend/Hub.Agent
```

Chạy thật thì đăng ký làm **Windows Service** để khởi động cùng máy (§3). Cùng một binary dùng được
cả hai cách — .NET tự nhận biết tiến trình có phải service hay không, không cần build riêng.

### Đặt khoá chung trước khi cài

Service chạy dưới `LocalSystem`, nên nó **không đọc được user-secrets** (user-secrets nằm trong hồ
sơ người dùng của bạn) và **không thấy** biến môi trường của tài khoản bạn. Nó chỉ đọc được biến
môi trường **cấp máy**.

**Không phải gõ hay dán khoá.** Script tự chép từ user-secrets của `Hub.Api` sang:

```powershell
# PowerShell với quyền Administrator
cd scripts
.\agent-service.ps1 set-secret
```

Lệnh này đọc `Agent:SharedSecret` từ user-secrets, ghi vào biến môi trường cấp máy
`Agent__SharedSecret`, rồi tự khởi động lại service (tiến trình chỉ đọc biến lúc khởi động). Dấu
`__` là cách .NET ánh xạ dấu `:` sang biến môi trường.

> **Vì sao không đưa lệnh `SetEnvironmentVariable` để bạn tự điền khoá vào:** đã thử và hỏng hai
> lần. Chỗ giữ chỗ trong khối lệnh bị dán nguyên si, khiến khoá thành một chuỗi vô nghĩa. Tệ hơn:
> service vẫn **Running**, `/agent/health` vẫn **200**, Event Log vẫn sạch — sai sót chỉ lộ ra lúc
> bấm nút tắt máy và nhận 401. Nên giờ script tự làm, không có gì để dán sai.

Kiểm chứng khoá đã khớp:

```powershell
.\agent-service.ps1 test-lock
```

Lệnh này gửi một lệnh `Lock` thật tới agent. Chọn `Lock` vì nó nhẹ nhất trong nhóm A — **màn hình sẽ
khoá**, mở lại bằng mật khẩu Windows, không tắt máy và không mất việc đang làm.

| Kết quả | Nghĩa là |
|---|---|
| Màn hình khoá + `HTTP 204` | ✅ Khoá khớp, agent nhận lệnh |
| `HTTP 401` | Khoá cấp máy khác khoá hub → chạy `set-secret` |
| `HTTP 503` | Agent chưa thấy khoá nào → chạy `set-secret` |
| Ký tự không hợp lệ | Đã dán nhầm chỗ giữ chỗ → chạy `set-secret` |

Nếu hub chưa có khoá nào trong user-secrets, `set-secret` sẽ báo và chỉ cách sinh khoá mới.

### Cài, gỡ, xem trạng thái

```powershell
# PowerShell với quyền Administrator
cd scripts
.\agent-service.ps1 install      # publish + đăng ký + khởi động
.\agent-service.ps1 set-secret   # chép khoá từ user-secrets + restart
.\agent-service.ps1 test-lock    # kiểm chứng khoá (không cần Administrator)
.\agent-service.ps1 status       # xem trạng thái (không cần Administrator)
.\agent-service.ps1 restart      # dùng sau khi đổi cấu hình
.\agent-service.ps1 uninstall    # dừng và xoá service
```

Lần đầu cài, chạy theo đúng thứ tự: **`install` → `set-secret` → `test-lock`**.

`install` publish bản Release vào `C:\ProgramData\DeviceHub\Agent` — **không** chạy thẳng từ thư mục
repo, vì repo có thể bị đổi nhánh hoặc xoá còn service thì trỏ vào một đường dẫn cố định. Đổi chỗ
bằng `-InstallPath`.

Service được đặt:

- **`delayed-auto`** — khởi động cùng máy nhưng hoãn một chút, để mạng và Tailscale kịp sẵn sàng
  (agent gọi hub ngay khi chạy).
- **Tự chạy lại khi lỗi** — sau 5 giây, 10 giây, rồi mỗi 60 giây. Không có cái này thì agent chết
  một lần là nằm im tới khi khởi động lại máy.

### Xem log

Service không có console, nên log đi vào **Windows Event Log**:

```powershell
Get-EventLog -LogName Application -Source 'Device Hub Agent' -Newest 20
```

Hoặc Event Viewer → Windows Logs → Application, lọc theo nguồn `Device Hub Agent`.

Đây là chỗ đầu tiên cần xem nếu service khởi động rồi tắt ngay.

### Kiểm chứng

```powershell
curl http://127.0.0.1:5199/agent/health     # phải trả 200
.\agent-service.ps1 status                  # phải thấy Running
```

Sau đó vào giao diện hub xem thiết bị có báo danh không (agent tự gọi
`POST /api/devices/register` mỗi lần khởi động).

## Chưa có: đánh thức máy (Wake)

§12 câu 10 yêu cầu **đo khả năng đánh thức bằng tay trước** khi viết phần này — "nếu phần cứng không
hỗ trợ thì code cũng vô ích". Việc cần đo trên từng máy:

- Đánh thức được từ trạng thái nào: sleep / hibernate / shutdown
- Qua dây hay Wi-Fi (WoL qua Wi-Fi thường không chạy)
- BIOS có bật Wake-on-LAN không
- Fast Startup của Windows có làm hỏng không

Agent đã thu thập sẵn **MAC** và **nhãn LAN** cho việc này, nên khi đo xong chỉ cần viết phần gửi
magic packet.

Lưu ý §5a.1: máy chạy hub **không tự đánh thức được chính nó**. Muốn đánh thức nó thì cần một waker
khác cùng LAN (NAS, hoặc laptop nếu đang bật).
