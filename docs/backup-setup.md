# Sao lưu lên Google Drive — năng lực 3

Hub gọi **rclone** để đồng bộ thư mục lên cloud (§2.3: tái sử dụng công cụ đã
được kiểm chứng, không tự viết client Drive).

---

## Vì sao `rclone sync`, không phải `rclone mount`

CONTEXT.md §5c dùng `rclone mount` cho việc **đọc** nhạc/phim từ cloud — mount
biến remote thành ổ đĩa ảo để `Hub.Music` đọc file như file cục bộ.

Sao lưu là việc khác, và lệnh khác:

| | `sync`/`copy` | `mount` + chép file |
|---|---|---|
| Kiểm tra toàn vẹn | Có, so hash | Không |
| Thử lại khi lỗi mạng | Có | Không |
| Truyền song song | Có | Không |
| Biết file nào đã lên | Có, số liệu chính xác | Không |
| Mount chết giữa chừng | — | Bản sao lưu hỏng **im lặng** |

Dòng cuối là lý do quyết định: sao lưu hỏng mà không báo gì là loại lỗi tệ nhất,
vì nó chỉ lộ ra vào đúng ngày cần khôi phục.

Hai công cụ **không mâu thuẫn** — chúng phục vụ hai năng lực khác nhau, và sẽ
cùng tồn tại khi §5c được dựng.

---

## Cài rclone

Tải bản Windows từ <https://rclone.org/downloads/>, giải nén, đặt ở nơi cố định:

```
D:\App\rclone\rclone.exe
```

Thêm vào **PATH cấp máy** — service HubApi chạy dưới LocalSystem, không thừa
hưởng PATH của phiên đăng nhập (cùng gốc với vấn đề config của cloudflared, xem
[services.md](services.md)):

```powershell
# Run as Administrator
$current = [Environment]::GetEnvironmentVariable('Path', 'Machine')
[Environment]::SetEnvironmentVariable('Path', "$current;D:\App\rclone", 'Machine')
```

Hoặc khai đường dẫn tuyệt đối trong cấu hình (`Backup:RclonePath`) — chắc chắn
hơn, không phụ thuộc PATH.

---

## Tạo client ID riêng của Google — bắt buộc

⚠️ **Không để trống `client_id` được nữa.** rclone khai tử client_id dùng chung:

> This shared client_id **is being retired and will stop working during 2026**.
> To avoid interruption you must create and use your own client_id, so creating
> one is now **required rather than merely recommended**.
> — [tài liệu rclone](https://rclone.org/drive/#making-your-own-client-id)

Bỏ trống thì `rclone config` trả lời `This value is required and it has no
default` và không đi tiếp được. Kể cả nếu lách qua được, backup sẽ chết vào một
ngày nào đó trong năm nay mà không báo trước — đúng kiểu hỏng tệ nhất với sao lưu.

Vào [Google API Console](https://console.developers.google.com/), làm một lần:

1. **Tạo/chọn project.**
2. **Bật Drive API** — *Library* → tìm "Google Drive API" → *Enable*.
3. **Cấu hình consent screen** — *OAuth consent screen* (hoặc *Audience* ở giao
   diện mới) → User type **External** → điền App name, User support email,
   Developer contact → Save.
4. **Thêm scope** — *Data Access* → *Add or remove scopes* → ô *Manually add
   scopes*, dán:
   ```
   https://www.googleapis.com/auth/docs,https://www.googleapis.com/auth/drive,https://www.googleapis.com/auth/drive.metadata.readonly
   ```
   → *Add to table* → *Update* → **Save**.
5. **Tạo OAuth client** — *Credentials* → *+ Create credentials* → *OAuth client
   ID* → Application type **Desktop app** → *Create*. Chép lại **Client ID** và
   **Client secret**.
6. **PUBLISH APP** — quay lại *OAuth consent screen* / *Audience*, bấm nút này.

### Ba lỗi đã gặp thật ở bước này

**`Error 400: redirect_uri_mismatch`** — chọn nhầm Application type. rclone xác
thực qua web server tạm ở `http://127.0.0.1:53682/auth`, và **chỉ loại Desktop
app** mới được Google cho phép redirect về localhost. Nếu màn hình tạo client bắt
nhập "Authorized redirect URIs" thì bạn đang chọn sai loại. Xoá client đó, tạo
lại đúng Desktop app.

**`Error 403: access_denied`** — app ở chế độ Testing và email của bạn chưa nằm
trong danh sách test user. Hai cách:

| | Thêm test user | **PUBLISH APP** |
|---|---|---|
| Token | **Hết hạn sau 7 ngày** | Không hết hạn |
| Google phê duyệt | Không cần | Không cần (dùng cá nhân, dưới 100 người) |
| Phiền toái | Xác thực lại mỗi tuần | Bấm qua cảnh báo một lần |

Với sao lưu thì **publish app**. Backup cứ 7 ngày lại chết âm thầm là thứ bạn chỉ
phát hiện vào đúng ngày cần khôi phục.

**"OAuth 2.0 Client IDs — No OAuth clients to display"** — Google không cho tạo
client cho tới khi consent screen (bước 3) xong. Làm bước 3 trước.

---

## Nối Google Drive

```powershell
rclone config
```

| Prompt | Nhập |
|---|---|
| `n/s/q>` | `n` |
| `name>` | `hub` |
| `Storage>` | `24` (Google Drive) |
| `Continue using the shared client_id anyway?` | `n` |
| `client_id>` | Client ID vừa tạo |
| `client_secret>` | Client secret vừa tạo |
| `scope>` | `1` (drive — toàn quyền) |
| `service_account_file>` | Enter (bỏ trống) |
| `Edit advanced config?` | `n` |
| `Use web browser to automatically authenticate?` | `y` |
| `Configure this as a Shared Drive (Team Drive)?` | `n` |
| `Keep this remote?` | `y` |

Trình duyệt mở ra. Gặp màn hình "Google chưa xác minh ứng dụng này" thì bấm
**Nâng cao** → **Chuyển đến … (không an toàn)** → cho phép. Đây là hành vi bình
thường của app chưa qua xác minh, không phải lỗi.

Kiểm tra bằng một lần gọi thật tới Drive:

```powershell
rclone lsd hub:
```

Phải liệt kê được thư mục trong Drive của bạn.

⚠️ **Chạy `rclone config` dưới tài khoản nào thì file cấu hình nằm trong hồ sơ
tài khoản đó.** Service chạy dưới LocalSystem sẽ không thấy — xem mục "Service
không thấy rclone.conf" bên dưới.

---

## Remote mã hoá cho dữ liệu nhạy cảm

Quyết định của dự án: **dữ liệu thường lưu nguyên bản** (để xem/tải trực tiếp từ
web Drive), **riêng thư mục dữ liệu của hub thì mã hoá**.

Lý do: `hub.db` chứa **hash mật khẩu và phiên đăng nhập**. Hash PBKDF2 không phải
plaintext, nhưng nó là thứ để tấn công offline — và Drive là tài khoản Google,
không phải máy của bạn.

Tạo remote `crypt` bọc lên `hub`:

```powershell
rclone config
```

| Prompt | Nhập |
|---|---|
| `n/s/q>` | `n` |
| `name>` | `hub-crypt` |
| `Storage>` | `16` (crypt) |
| `remote>` | `hub:hub-data-encrypted` |
| `filename_encryption>` | `1` (standard) |
| `directory_name_encryption>` | `1` (true) |
| `Password` | `y` → tự đặt (KHÔNG dùng lại mật khẩu hub) |
| `Password for salt` | `g` → `128` → `y` |
| `Edit advanced config?` | `n` |
| `Keep this remote?` | `y` |

⚠️ **Mất mật khẩu crypt hoặc chuỗi salt là mất dữ liệu.** Không có cách khôi
phục. Lưu chúng ở nơi khác với máy này — lưu chỉ trên chính máy đang sao lưu thì
bản sao lưu vô dụng đúng lúc cần nhất.

### Kiểm chứng mã hoá có thật không

Đừng tin, hãy nhìn. So sánh cùng dữ liệu qua hai remote:

```powershell
rclone ls hub-crypt:hub-data        # qua crypt: thấy tên file thật
rclone lsf hub:hub-data-encrypted -R --files-only   # thô: đúng thứ Google thấy
```

Lệnh thứ hai phải ra tên vô nghĩa, ví dụ:

```
nmqspq21ib4aciggdneli30ujc/mo0q53mb4hiok6d5vsv68d4pic
```

Đọc nội dung thô thì thấy magic `RCLONE\0\0` rồi toàn byte nhiễu. Nếu lệnh thứ
hai vẫn hiện `hub.db` thì **mã hoá chưa có tác dụng** — kiểm tra lại
`Destination` của job có trỏ vào remote crypt không.

---

## Khai công việc sao lưu

Vào `appsettings.Production.json` trong thư mục dữ liệu (`D:\App\HubData` theo
cấu hình hiện tại — xem [services.md](services.md)):

```json
{
  "Backup": {
    "RclonePath": "D:\\App\\rclone\\rclone.exe",
    "ConfigPath": "C:\\Windows\\System32\\config\\systemprofile\\AppData\\Roaming\\rclone\\rclone.conf",
    "HistoryLimit": 50,
    "TimeoutMinutes": 120,
    "Jobs": [
      {
        "Name": "tai-lieu",
        "Source": "D:\\Tai lieu",
        "Destination": "hub:backup/tai-lieu",
        "Encrypted": false
      },
      {
        "Name": "hub-data",
        "Source": "D:\\App\\HubData",
        "Destination": "hub-crypt:hub-data",
        "Encrypted": true
      }
    ]
  }
}
```

| Khoá | Ý nghĩa |
|---|---|
| `Name` | Tên hiển thị, cũng là định danh trong API. Không được trùng. |
| `Source` | Thư mục trên máy này |
| `Destination` | `remote:đường/dẫn` của rclone |
| `Encrypted` | Đích có phải remote crypt không — dùng để hiển thị và cảnh báo |
| `DeleteExtra` | `true` = `rclone sync` (xoá file thừa ở đích), `false` = `copy` |
| `Enabled` | `false` để tắt tạm mà không xoá cấu hình |

### `DeleteExtra` — mặc định `false` là có chủ đích

`sync` xoá file ở đích khi nguồn không còn. Nghĩa là **xoá nhầm ở máy sẽ lan lên
cloud**, và bản sao lưu mất luôn giá trị cứu hộ.

Chỉ bật khi thực sự cần gương đúng trạng thái nguồn, và hiểu rõ đánh đổi.

---

## Dùng

```
GET  /api/backup                  trạng thái các job + lần chạy gần nhất
GET  /api/backup/history?limit=20 lịch sử
POST /api/backup/{tên}/run        chạy một job
```

Mọi endpoint yêu cầu đăng nhập; `POST` cần thêm antiforgery token (§6.5 mục 5).

Request `run` giữ kết nối tới khi rclone xong — có thể vài phút. Huỷ request sẽ
**giết luôn tiến trình rclone**, nên đừng huỷ giữa chừng trừ khi cố ý.

---

## Kiểm tra cấu hình lúc khởi động

Hub kiểm tra và ghi log ngay khi khởi động, vì sai cấu hình sao lưu chỉ lộ ra
vào ngày cần khôi phục:

| Tình huống | Mức |
|---|---|
| Sao lưu thư mục dữ liệu hub lên đích **không mã hoá** | Lỗi |
| Tên job trùng nhau | Lỗi |
| Thiếu `Source` hoặc `Destination` | Lỗi |
| Nguồn không tồn tại | Cảnh báo (ổ ngoài chưa cắm là bình thường) |

Xem log hub sau khi đổi cấu hình.

---

## Trạng thái một lần chạy

| Trạng thái | Nghĩa |
|---|---|
| `Succeeded` | rclone xong, **không lỗi nào** |
| `Failed` | rclone trả mã khác 0, **hoặc** có lỗi lẻ tẻ |
| `Cancelled` | Quá `TimeoutMinutes`, hoặc hub tắt giữa chừng |

⚠️ **Lỗi lẻ tẻ vẫn tính là `Failed`.** rclone có thể trả mã 0 mà vẫn báo vài file
không sao lưu được (file đang bị khoá, chẳng hạn). Báo "thành công" trong trường
hợp đó là nói dối: người dùng sẽ tin vào một bản sao lưu thiếu.

---

## Khi gặp trục trặc

### `rcloneAvailable: false`

Hub không gọi được rclone. Kiểm tra:

```powershell
rclone version                                    # trong phiên của bạn
[Environment]::GetEnvironmentVariable('Path','Machine')   # service thấy gì
```

Chắc chắn nhất là khai `Backup:RclonePath` bằng đường dẫn tuyệt đối.

### Service không thấy `rclone.conf`

`rclone config` ghi vào hồ sơ của tài khoản chạy nó. Service chạy dưới
`LocalSystem` nên tìm ở:

```
C:\Windows\System32\config\systemprofile\AppData\Roaming\rclone\rclone.conf
```

Hai cách:

- Chép file cấu hình sang đó, **hoặc**
- Khai `Backup:ConfigPath` trỏ vào file trong hồ sơ của bạn

Cách thứ hai đơn giản hơn nhưng file phải cho `SYSTEM` đọc được. Cách thứ nhất,
làm trong PowerShell **Run as Administrator**:

```powershell
$src = Join-Path $env:APPDATA 'rcloneclone.conf'
$dstDir = 'C:\Windows\System32\config\systemprofile\AppData\Roamingclone'
New-Item -ItemType Directory $dstDir -Force | Out-Null
Copy-Item $src (Join-Path $dstDir 'rclone.conf') -Force
```

⚠️ **Siết quyền đọc sau khi chép.** File này chứa token OAuth của Google Drive và
mật khẩu crypt (dạng obscure) — bất cứ ai đọc được nó đều mở được bản sao lưu.
Bỏ thừa kế và chỉ giữ `SYSTEM`, `Administrators`, chủ sở hữu.

**Kiểm chứng SYSTEM thật sự đọc được**, đừng chỉ tin là đã chép xong. Tạo một
scheduled task chạy dưới `SYSTEM` gọi `rclone listremotes` và xem kết quả — nếu
nó liệt kê đủ cả remote thường lẫn remote crypt thì service sẽ chạy được.

⚠️ Sửa cấu hình rclone sau này (thêm remote, đổi mật khẩu) thì **phải chép lại** —
bản trong hồ sơ hệ thống không tự cập nhật.

Cách thứ hai đơn giản hơn, nhưng file phải cho `SYSTEM` đọc được.

### Job kẹt ở "đang chạy"

Hub tự dọn lúc khởi động — bản ghi treo được đánh dấu `Cancelled`. Nếu vẫn kẹt
khi hub đang chạy thì đó là rclone thật sự chưa xong; chờ tới `TimeoutMinutes`.

### `/api/backup` trả 404 dù cấu hình đúng

Không phải lỗi cấu hình — **service đang chạy binary cũ hơn tính năng**. Endpoint
backup chỉ có trong bản publish từ commit thêm năng lực 3 trở đi; bản cũ không hề
chứa route đó.

Đã gặp thật: service chạy binary build ngày 3, còn code backup commit ngày 10.

Kiểm tra trong một phút:

```powershell
# Binary đang chạy build lúc nào
Get-Item backend\Hub.Apiin\Release
et10.0\publish\Hub.Api.dll | Select-Object LastWriteTime

# DLL đã publish có chứa endpoint không (0 = chưa có)
Select-String -Path backend\Hub.Apiin\Release
et10.0\publish\Hub.Api.dll -Pattern 'MapBackupEndpoints' -AllMatches |
    Measure-Object | Select-Object Count
```

Sửa bằng cách publish lại rồi tráo vào — xem "Cập nhật hub khi service đang chạy"
trong [services.md](services.md):

```powershell
dotnet publish backend/Hub.Api/Hub.Api.csproj -c Release `
    -o backend/Hub.Api/bin/Release/net10.0/publish-new

.\scripts\hub-services.ps1 restart -Only hub    # Administrator
```

Dấu hiệu phân biệt với lỗi xác thực: `/api/devices` vẫn trả **200** trong khi
`/api/backup` trả **404**. Phiên đăng nhập không có vấn đề gì.

### `appsettings.Production.json` sai cú pháp

Hub **vẫn khởi động** và ghi một dòng lỗi vào log. Nó không dùng file đó, nên
mọi cấu hình bên trong (kể cả `MeshCentral:Url`) sẽ thiếu.

Kiểm tra cú pháp trước khi lưu:

```powershell
Get-Content D:\App\HubData\appsettings.Production.json | ConvertFrom-Json
```

---

## Chưa làm

- **Lịch tự động.** Hiện phải bấm chạy. Muốn tự động thì dùng Task Scheduler gọi
  API, hoặc chờ tính năng lịch trong hub.
- **Khôi phục từ giao diện.** Khôi phục làm bằng `rclone copy` theo chiều ngược
  lại, ngoài hub. Đó là chủ đích: khôi phục là thao tác hiếm và nguy hiểm, không
  nên chỉ cách một cú bấm.
- **`rclone mount`** cho năng lực 8/9 (§5c) — cần WinFsp cài sẵn trên Windows.
