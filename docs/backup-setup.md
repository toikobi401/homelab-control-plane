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

## Nối Google Drive

```powershell
rclone config
```

Chọn `n` (new remote), đặt tên `gdrive`, chọn `drive`, để trống client_id và
client_secret (dùng mặc định của rclone), chọn scope `1` (toàn quyền), rồi làm
theo bước xác thực trên trình duyệt.

Kiểm tra:

```powershell
rclone lsd gdrive:
```

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

Tạo remote `crypt` bọc lên `gdrive`:

```powershell
rclone config
# n → tên: gdrive-crypt → storage: crypt
# remote: gdrive:hub-data-encrypted
# filename_encryption: standard
# directory_name_encryption: true
# Đặt mật khẩu (KHÔNG dùng lại mật khẩu hub)
```

⚠️ **Mất mật khẩu crypt là mất dữ liệu.** Không có cách khôi phục. Lưu nó ở nơi
khác với máy này — nếu chỉ lưu trên chính máy đang sao lưu thì bản sao lưu vô
dụng đúng lúc cần nhất.

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
        "Destination": "gdrive:backup/tai-lieu",
        "Encrypted": false
      },
      {
        "Name": "hub-data",
        "Source": "D:\\App\\HubData",
        "Destination": "gdrive-crypt:",
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

Cách thứ hai đơn giản hơn, nhưng file phải cho `SYSTEM` đọc được.

### Job kẹt ở "đang chạy"

Hub tự dọn lúc khởi động — bản ghi treo được đánh dấu `Cancelled`. Nếu vẫn kẹt
khi hub đang chạy thì đó là rclone thật sự chưa xong; chờ tới `TimeoutMinutes`.

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
