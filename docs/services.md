# Chạy hub như dịch vụ nền

**Vấn đề:** khởi động lại máy host thì Cloudflare trả `Error 1033 — Cloudflare
Tunnel error`, hub và MeshCentral đều không vào được.

Nguyên nhân: `cloudflared` và hub chạy thủ công trong một cửa sổ terminal, nên
tắt máy là mất. Chúng phải là Windows Service.

---

## Ba tiến trình, ba cơ chế

| Thành phần | Service | Cài bằng |
|---|---|---|
| Tailscale | Có sẵn | Trình cài đặt |
| Mesh Agent | Có sẵn | Trình cài đặt |
| MeshCentral | **Đã có** — `meshcentral.exe` | `node node_modules/meshcentral --install` |
| Hub API | Cài bằng script | `hub-services.ps1 install` |
| Cloudflared | Cài bằng script | `hub-services.ps1 install` |

MeshCentral **đã là service từ trước** và tự chạy lại sau reboot — không cần cài
lại. Xem phần "Bẫy cổng 4431" bên dưới để hiểu vì sao có lúc tưởng nó không chạy.

---

## Cài

Publish hub trước — service chạy `Hub.Api.exe`, không chạy được `dotnet run`:

```powershell
dotnet publish backend/Hub.Api/Hub.Api.csproj -c Release `
    -o backend/Hub.Api/bin/Release/net10.0/publish
```

Rồi, trong PowerShell mở bằng **Run as Administrator**:

```powershell
.\scripts\hub-services.ps1 install
.\scripts\hub-services.ps1 status
```

Các lệnh khác:

```powershell
.\scripts\hub-services.ps1 status              # không cần Administrator
.\scripts\hub-services.ps1 restart -Only hub   # sau khi publish lại
.\scripts\hub-services.ps1 uninstall
```

`status` in cả trạng thái ba service lẫn cổng đang lắng nghe — cổng mới là thứ
nói lên hệ thống có thật sự phục vụ được hay không.

---

## Bẫy cổng 4431 — hai bản MeshCentral cùng chạy

Sau reboot, cổng 4430 im lặng dù service `meshcentral.exe` vẫn `Running`. Log cho
thấy nó đang nghe ở **4431 và 8009** thay vì 4430/8008.

Lý do nằm ở `webserver.js` dòng 9294:

```javascript
else { if (port < 65535) { CheckListenPort(port + 1, addr, func); } else { ... } }
```

MeshCentral thấy cổng bị chiếm thì **lặng lẽ nhảy sang cổng kế tiếp** — không
báo lỗi, không cảnh báo. Tunnel trỏ vào 4430 nên không thấy gì, còn service thì
vẫn hiện `Running` bình thường.

Cổng bị chiếm bởi một bản MeshCentral thứ hai chạy tay để gỡ rối. Chính nó đẩy
service xuống 4431.

**Quy tắc:** đừng chạy `node node_modules/meshcentral` bằng tay khi service đang
chạy. Muốn khởi động lại thì dùng:

```powershell
Restart-Service meshcentral.exe
```

`hub-services.ps1 status` có kiểm tra riêng cổng 4431 và cảnh báo nếu thấy nó mở.

---

## MeshCentral im lặng sau reboot — lỗi 502

**Triệu chứng:** sau khi khởi động lại máy, `mesh.` trả **502** (khác 1033).
Service `meshcentral.exe` hiện `Running`, nhưng **không cổng nào lắng nghe**.

502 và 1033 nói hai chuyện khác nhau:

| Mã | Nghĩa |
|---|---|
| **1033** | cloudflared không chạy — không tunnel nào kết nối tới Cloudflare |
| **502** | tunnel có kết nối, nhưng không tới được dịch vụ phía sau |

**Nguyên nhân — cuộc đua khởi động.** MeshCentral khai `portbind` là địa chỉ
tailnet (`100.121.227.63`). Sau reboot cả `Tailscale` lẫn `meshcentral.exe` đều
`AUTO_START` nên chạy song song. MeshCentral bind vào một địa chỉ **chưa tồn
tại**, thất bại, và **không thử lại** — mã nguồn không có cơ chế retry.

Điều khiến nó khó đoán: MeshCentral **không sập**. Nó chạy tiếp bình thường, chỉ
là không phục vụ gì. Service vẫn `Running`, log không có lỗi.

### Cách sửa

```powershell
.\scripts\hub-services.ps1 fix-mesh-startup    # Administrator
```

Bốn lớp, mỗi lớp bịt một khoảng trống của lớp trước:

| Lớp | Làm gì | Không đủ vì |
|---|---|---|
| 1. Phụ thuộc `Tailscale` | Tailscale khởi động trước | chỉ đảm bảo đã KHỞI ĐỘNG, không phải đã GÁN XONG địa chỉ |
| 2. Delayed auto-start | hoãn ~2 phút | thời gian gán địa chỉ không cố định |
| 3. Recovery action | chạy lại khi tiến trình sập | MeshCentral **không sập** khi bind hỏng |
| 4. Canh chừng cổng | kiểm tra 4430 thật, tối đa 5 phút | — |

Lớp 4 là lớp duy nhất phát hiện được đúng trạng thái hỏng này. Nó là scheduled
task `MeshCentral-Watchdog` chạy lúc khởi động dưới `SYSTEM`, gọi
`D:\App\MeshCentral\mesh-watchdog.ps1`.

### Chữa nhanh khi đang gặp

```powershell
Restart-Service meshcentral.exe -Force
```

Khởi động lại lúc Tailscale đã sẵn sàng thì bind được ngay.

### Phân biệt với bẫy cổng 4431

Hai lỗi khác nhau, cùng triệu chứng "mesh không vào được":

- **4431 đang mở** → có hai bản MeshCentral, bản sau bị đẩy sang cổng kế tiếp
- **không cổng nào mở** → cuộc đua khởi động ở mục này

`hub-services.ps1 status` phân biệt được: nó in dòng `MeshCentral (cổng 4430)`
và cảnh báo riêng nếu thấy 4431.

---

## Vì sao hub cần UseWindowsService()

`Program.cs` gọi:

```csharp
builder.Host.UseWindowsService();
```

Thiếu dòng này thì service **kẹt mãi ở `Start Pending`**: hub vẫn khởi động và
phục vụ bình thường (cổng mở, `/health` trả 200), nhưng nó không bao giờ báo
"đã sẵn sàng" cho Service Control Manager. Hậu quả:

- `Stop-Service` và `Restart-Service` treo cho tới khi hết giờ
- Windows có thể tự giết tiến trình vì tưởng nó khởi động hỏng
- `status` hiện `Start Pending` vô thời hạn dù mọi thứ đang chạy

Nếu gặp trạng thái này (bản hub cũ), thoát ra bằng cách giết thẳng tiến trình —
`hub-services.ps1` có `Stop-HubCompletely` làm đúng việc đó.

Gói `Microsoft.Extensions.Hosting.WindowsServices` an toàn trên Linux: chạy
ngoài Windows thì nó không làm gì, nên `dotnet run` lúc phát triển và container
trên NAS đều không bị ảnh hưởng.

## Cập nhật hub khi service đang chạy

Không publish đè lên `Hub.Api.exe` mà service đang giữ được. Publish ra thư mục
tạm rồi để script tráo vào:

```powershell
dotnet publish backend/Hub.Api/Hub.Api.csproj -c Release `
    -o backend/Hub.Api/bin/Release/net10.0/publish-new

.\scripts\hub-services.ps1 restart -Only hub    # Administrator
```

`restart` và `install` đều tự phát hiện `publish-new`, dừng service, tráo thư
mục, rồi chạy lại.

## Hai bẫy khi cài service cho cloudflared

**1. `cloudflared service install` tạo service thiếu tham số.**

Lệnh sẵn có của cloudflared đặt `binPath` chỉ gồm đường dẫn exe, không có
`tunnel run`. Service khởi động lên chỉ in trợ giúp rồi thoát:

```
use `cloudflared tunnel run` to start tunnel f962dbe6-...
```

Trạng thái hiện `Stopped` mà Event Log không ghi lỗi gì — rất khó đoán. Script
tự dựng service với binPath đầy đủ thay vì dùng lệnh đó.

**2. LocalSystem không thấy config trong thư mục người dùng.**

Service chạy dưới `LocalSystem`, còn config nằm ở `C:\Users\<tên>\.cloudflared`.
cloudflared tìm config trong `.cloudflared` của **chính tài khoản đang chạy**,
nên phải chép sang hồ sơ hệ thống:

```
C:\Windows\System32\config\systemprofile\.cloudflared\
```

Script chép cả `config.yml` lẫn file credentials `.json`.

⚠️ Sửa `config.yml` ở thư mục người dùng thì phải chạy lại `install` để chép
sang, nếu không service vẫn dùng bản cũ.

**Ghi chú về `sc.exe`:** tạo service bằng `sc.exe create` với binPath có khoảng
trắng sẽ hỏng — PowerShell tách chuỗi thành nhiều tham số trước khi `sc.exe`
nhận được, gây lỗi `1639` (cú pháp sai). Dùng `New-Service` thì chuỗi giữ
nguyên vẹn.

---

## Vì sao hub cần biến môi trường cấp máy

Hub đọc chế độ bind từ `HUB_BIND_MODE` (xem
`Hosting/NetworkBinding.cs`). Service **không thừa hưởng biến môi trường của
phiên đăng nhập**, nên script đặt nó ở cấp máy:

```powershell
[Environment]::SetEnvironmentVariable('HUB_BIND_MODE', 'Tunnel', 'Machine')
```

Thiếu biến này thì hub rơi về `Localhost`, không mở cổng 7190, và tunnel lại báo
1033 y như cũ.

⚠️ Tên biến là `HUB_BIND_MODE` — có gạch dưới giữa `BIND` và `MODE`.

## Hub "mất dữ liệu" sau khi cài service

**Triệu chứng:** hub hiện *"Lần đầu chạy hub. Đặt mật khẩu ngay trên máy chạy
hệ thống"* dù trước đó đã có mật khẩu.

**Dữ liệu không mất.** `HubPaths.ResolveDataDirectory` mặc định dùng
`LocalApplicationData`, mà giá trị đó phụ thuộc tài khoản đang chạy:

| Chạy dưới | LocalApplicationData |
|---|---|
| Người dùng `ledat` | `C:\Users\ledat\AppData\Local` |
| **LocalSystem (service)** | `C:\Windows\System32\config\systemprofile\AppData\Local` |

Service nhìn vào thư mục thứ hai, thấy trống, nên tưởng lần đầu chạy. `hub.db`
cũ vẫn nằm nguyên ở thư mục người dùng.

⚠️ **Đừng bấm đặt mật khẩu mới ở màn hình đó** — nó tạo một DB rỗng thứ hai, và
sau đó phải gộp thủ công.

**Cách sửa:** khai `HUB_DATA_DIR` ở cấp máy. `hub-services.ps1 install` tự làm,
trỏ về `D:\App\HubData`.

Chuyển dữ liệu cũ sang trước khi cài (chép cả thư mục `certs` nếu có):

```powershell
New-Item -ItemType Directory D:\App\HubData -Force
Copy-Item "$env:LOCALAPPDATA\Hub\hub.db" D:\App\HubData\
Copy-Item "$env:LOCALAPPDATA\Hub\certs" D:\App\HubData\ -Recurse
```

`hub-services.ps1 status` in thư mục dữ liệu và cảnh báo nếu không thấy
`hub.db` ở đó — kiểm tra dòng này trước khi mở giao diện.

Xác nhận hub đọc đúng DB:

```powershell
curl.exe -s https://hub.youtubecontentgen.io.vn/api/auth/status
# {"passwordConfigured":true}  <- doc duoc DB cu
```

Cùng gốc vấn đề với cloudflared ở trên: **service không chạy dưới tài khoản của
bạn**, nên mọi đường dẫn suy ra từ hồ sơ người dùng đều trỏ sai chỗ.

## Thiết bị biến mất sau khi cài service

**Triệu chứng:** `/devices` trống và `/remote` không nhúng được MeshCentral, dù
hub đăng nhập bình thường.

**Không phải mất dữ liệu.** `/devices` đọc trực tiếp từ **Tailscale API**, không
từ `hub.db` (bảng `Devices` đã bị xoá ở migration `RemoveDeviceControlTables`
khi chuyển sang MeshCentral). Nó cần credentials Tailscale để gọi được.

Bí mật nằm trong **user-secrets**, mà user-secrets gắn với hồ sơ người dùng:

```
C:\Users\<tên>\AppData\Roaming\Microsoft\UserSecrets\<id>\secrets.json
```

Service chạy dưới `LocalSystem` nên không đọc được file này. Sáu giá trị bị mất:

| Khoá | Thiếu thì sao |
|---|---|
| `Tailscale:ClientId` / `ClientSecret` | `/devices` trống |
| `MeshCentral:Url` / `PublicUrl` | `/remote` không nhúng được |
| `HUB_TLS_CERT` / `HUB_TLS_KEY` | chế độ bind Tailnet không chạy |

**Cách sửa:** đặt `appsettings.Production.json` cạnh `hub.db`. `Program.cs` nạp
thêm nguồn cấu hình này:

```csharp
builder.Configuration.AddJsonFile(
    Path.Combine(dataDirectory, "appsettings.Production.json"),
    optional: true, reloadOnChange: false);
```

`optional: true` nên `dotnet run` lúc dev vẫn dùng user-secrets như cũ.

Sinh file từ user-secrets (khoá phẳng `A:B` thành lồng nhau):

```json
{
  "Tailscale": { "ClientId": "...", "ClientSecret": "..." },
  "MeshCentral": {
    "Url": "https://<tên-máy>.<tailnet>.ts.net:4430",
    "PublicUrl": "https://mesh.tenmien-cua-ban.com"
  },
  "HUB_TLS_CERT": "D:\App\HubData\certs\<tên>.crt",
  "HUB_TLS_KEY": "D:\App\HubData\certs\<tên>.key"
}
```

⚠️ Đường dẫn chứng chỉ phải trỏ vào thư mục dữ liệu, **không** vào
`%LOCALAPPDATA%` — service không đọc được chỗ đó. Chép cả thư mục `certs` sang.

⚠️ **File này chứa token Tailscale.** `install` tự bỏ thừa kế quyền và chỉ giữ
SYSTEM, Administrators, chủ sở hữu. Nó nằm ngoài repo nên không bị commit nhầm.

Đặt cạnh `hub.db` chứ không trong thư mục cài đặt: nó sống sót qua mỗi lần
publish đè, và sao lưu cùng chỗ với dữ liệu.

`hub-services.ps1 status` báo đỏ nếu thiếu file này.

## Vì sao wwwroot phải nằm cạnh binary

`Program.cs` đặt content root theo vị trí binary chứ không theo thư mục làm việc:

```csharp
ContentRootPath = AppContext.BaseDirectory
```

Đó là chủ ý — service chạy với thư mục làm việc là `system32`. Hệ quả: `wwwroot`
phải nằm **cùng chỗ với `Hub.Api.exe`**. `dotnet publish` tự lo việc này; chép
tay vào thư mục dự án thì hub không thấy và mọi đường dẫn trả 404 trong khi
`/health` vẫn 200.

---

## Tự chạy lại khi sập

Service hub được đặt tự khởi động lại sau 5 giây, 10 giây, rồi 30 giây:

```powershell
sc.exe failure HubApi reset= 86400 actions= restart/5000/restart/10000/restart/30000
```

Đây là mạng an toàn cho sự cố nhất thời, không phải thứ thay cho việc sửa lỗi —
tiến trình sập lặp lại thì phải xem log, đừng dựa vào nó tự dậy.

---

## Khi vẫn không vào được

Theo thứ tự, dừng ở bước đầu tiên sai:

```powershell
.\scripts\hub-services.ps1 status          # service nào chưa chạy?
Get-NetTCPConnection -State Listen -LocalPort 4430,7190
curl.exe -s -o NUL -w "%{http_code}" https://hub.youtubecontentgen.io.vn/health
```

- **Service `Stopped`** → xem Event Viewer, hoặc chạy `Hub.Api.exe` bằng tay để thấy lỗi
- **Service `Running` nhưng cổng không mở** → sai `HUB_BIND_MODE`, hoặc MeshCentral bị đẩy sang 4431
- **Cổng mở nhưng vẫn 1033** → cloudflared chưa chạy hoặc mất kết nối tới biên
