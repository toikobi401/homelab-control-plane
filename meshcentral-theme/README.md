# Theme Device Hub cho MeshCentral

Làm MeshCentral trông như cùng một ứng dụng với hub, thay vì hai app lồng nhau
khi nhúng ở tab **Điều khiển**.

## Cài

```powershell
.\deploy.ps1                       # chép sang D:\App\MeshCentral
.\deploy.ps1 -MeshCentralPath X:\… # nếu cài chỗ khác
.\deploy.ps1 -Remove               # gỡ, về diện mạo gốc
```

**Không cần khởi động lại MeshCentral** — file tĩnh đọc theo từng request.

### Nhưng PHẢI xoá cache trình duyệt

MeshCentral gửi `Cache-Control: max-age=14400` cho `custom.css`, nên trình duyệt
giữ bản cũ **4 tiếng**. Không ép nạp lại thì trông y hệt như theme không có tác
dụng — đây là bẫy đã làm mất thời gian một lần.

```
Ctrl + Shift + R
```

Hoặc DevTools → Network → tick *Disable cache* → F5.

**Cách kiểm chứng theme đã vào chưa** (dán vào Console):

```js
[...document.styleSheets].find(s => (s.href||'').includes('custom.css')).cssRules.length
```

Ra `0` là đang dùng bản cache. Ra số lớn hơn 0 là theme đã hoạt động.

## Cách nó hoạt động

MeshCentral **luôn nạp** `styles/custom.css` và `scripts/custom.js` trong mọi
trang, và `custom.css` là stylesheet **cuối cùng** trong `<head>` — cả hai điều
này đo được bằng trình duyệt thật, không phải suy đoán từ tài liệu.

Nhờ vậy ta thắng về độ ưu tiên CSS mà gần như không cần `!important`.

Hai file này trong `node_modules/meshcentral/public/` vốn **rỗng** — chúng sinh
ra để người dùng ghi đè.

### Vì sao không dùng `meshcentral-web/`

Đó là thư mục override chính thức, và lẽ ra là chỗ đúng. Nhưng **nó không hoạt
động trên cài đặt này** — đã kiểm chứng: đặt một file thử vào
`meshcentral-web/public/styles/` rồi gọi qua HTTP thì server trả **404**, dù
đường dẫn, quyền đọc và thứ tự middleware đều đúng.

Nghi do service chạy từ `WinService\daemon\meshcentral.exe` nên `__dirname` mà
MeshCentral dùng để dò override lệch khỏi chỗ ta đặt file.

**Đánh đổi:** `npm update meshcentral` sẽ ghi đè hai file. Chạy lại `deploy.ps1`
sau mỗi lần cập nhật. Script tự lưu bản gốc (`.orig`) lần đầu nên `-Remove` trả
về đúng nguyên trạng.

### Vì sao không phải `sitestyle: 4`

MeshCentral **chỉ hiểu `sitestyle` 2 (Classic) và 3 (Modern)** — kiểm bằng cách
tìm trong `webserver.js`: không có `sitestyle == 4`, không có `default4`. Đặt 4
sẽ rơi vào nhánh `>= 2` và chạy Classic.

Để có "sitestyle 4" thật phải sửa định tuyến trong `node_modules` và bảo trì một
bản sao `default3.handlebars` **1,7 triệu ký tự** — `npm update` xoá sạch, và
mỗi lần MeshCentral sửa lỗi ta không được hưởng. Đường qua `custom.css` đạt cùng
kết quả về diện mạo mà không mang gánh nặng đó.

## Phạm vi

Chỉ đụng **lớp trình bày**: màu, font, bo góc, spacing, vùng chạm.

Không đổi bố cục, không ẩn nút, không chạm hành vi. Hỏng thì `.\deploy.ps1
-Remove` là về nguyên trạng.

## Những gì đã đo trên trang thật

**Trang chính** (dùng `style-bootstrap.css`):

| Phần tử | Trước | Sau |
|---|---|---|
| `#page_leftbar` | `linear-gradient(#104893 → #113962)` | `#171717` |
| `#masthead` | `rgb(0,51,102)` navy | `#171717` |
| `#footer` | `rgb(17,57,98)` | `#171717` |
| `--bs-border-radius` | `0.375rem` | `10px` |
| Font | `system-ui` | Inter |

**Trang đăng nhập** (dùng `style.css` — Classic, KHÔNG có Bootstrap):

| Phần tử | Trước | Sau |
|---|---|---|
| `body.login` | gradient xanh | `#0a0a0a` |
| `#backgroundImage` | `welcome.png` | ẩn |
| `#loginpanel` | `rgb(151,151,151)` | `#171717` viền mảnh |
| `#username`, `#password` | `rgb(255,248,204)` vàng | `#0a0a0a` |

### Ba bẫy đã gặp

1. **Sidebar là `#page_leftbar`**, không phải `#topbar` như tên gợi ý.
2. **Nó dùng `background-image: linear-gradient`** — đặt `background-color`
   không có tác dụng, phải ghi đè `background` (thuộc tính gộp).
3. **Trang đăng nhập không nạp Bootstrap.** Nó dùng `style.css` (Classic), nên
   mọi biến `--bs-*` vô tác dụng ở đó — phải nhắm thẳng từng id.

Cả ba đều tìm ra bằng cách quét `getComputedStyle` trên trang thật, không phải
đọc tài liệu hay đoán từ tên phần tử.

## Giới hạn đã biết

- **Icon sidebar là ảnh PNG**, không đổi màu bằng CSS được. Chúng vẫn đọc được
  trên nền tối nên để nguyên.
- **Nút `.btn-primary` của MeshCentral** ở vài chỗ dùng class riêng, chưa phủ
  hết — cần kiểm chứng thêm sau khi restart.
- **Trang đăng nhập** dùng template `login2.handlebars` riêng; CSS đã nhắm tới
  nhưng chưa xem được sau restart.
