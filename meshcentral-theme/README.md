# Theme Device Hub cho MeshCentral

Làm MeshCentral trông như cùng một ứng dụng với hub, thay vì hai app lồng nhau
khi nhúng ở tab **Điều khiển**.

## Cài

```powershell
.\deploy.ps1                       # chép sang D:\App\MeshCentral
.\deploy.ps1 -MeshCentralPath X:\… # nếu cài chỗ khác
.\deploy.ps1 -Remove               # gỡ, về diện mạo gốc
```

Sau đó **hai bước nữa** — thiếu bước nào cũng trông như theme không có tác dụng.

### 1. Khởi động lại MeshCentral (quyền admin)

```powershell
Restart-Service "meshcentral.exe"
```

MeshCentral **chốt danh sách file web lúc khởi động**:

- File **thêm mới** → không được nhận cho tới lần restart kế tiếp.
- File **ghi đè** lên chỗ đã có → đọc lại được ngay, không cần restart.

Nên lần đầu tạo `meshcentral-web/` thì bắt buộc restart; các lần cập nhật sau
chỉ cần `Ctrl+Shift+R`.

### 2. Xoá cache trình duyệt

```
Ctrl + Shift + R
```

`custom.css` được gửi kèm `Cache-Control: max-age=14400`, nên trình duyệt giữ
bản cũ **4 tiếng**. Hoặc DevTools → Network → tick *Disable cache* → F5.

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

### Chép vào cả hai đích

`deploy.ps1` chép vào **hai** chỗ, mỗi chỗ một vai trò:

| Đích | Vai trò |
|---|---|
| `meshcentral-web/public/` | Thư mục override chính thức. MeshCentral **ưu tiên** chỗ này, và `npm update` không đụng. |
| `node_modules/meshcentral/public/` | Nơi phục vụ mặc định. Dự phòng nếu override không được nhận. |

**Một kết luận sai đã sửa.** Có lúc script chỉ chép vào `node_modules` vì phép
thử cho thấy file mới trong `meshcentral-web` trả 404. Kết luận đó **sai** —
override vẫn hoạt động; MeshCentral chỉ **chốt danh sách file lúc khởi động**,
nên file *thêm mới* không được nhận cho tới lần restart kế tiếp.

Bằng chứng dứt điểm: sau restart, ETag server trả về là `4d3e` = **19774 byte**,
khớp đúng file trong repo. Trước khi sửa script, ETag là `2f35` = 12085 byte —
đúng kích thước bản cũ nằm trong `meshcentral-web`, tức là **override đang che
mất bản mới trong `node_modules`**.

Đó là lý do phải chép cả hai: quên một chỗ thì bản cũ ở đó thắng.

**Đánh đổi:** `npm update meshcentral` ghi đè bản trong `node_modules` (bản
trong `meshcentral-web` thì không). Chạy lại `deploy.ps1` sau mỗi lần cập nhật.

### Mẹo chẩn đoán

So kích thước server trả về với file trong repo:

```powershell
(Invoke-WebRequest 'https://<mesh>/styles/custom.css?v=1' -UseBasicParsing).Content.Length
```

Lệch nhiều nghĩa là đang phục vụ bản cũ ở đích còn lại.

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
