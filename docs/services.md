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
