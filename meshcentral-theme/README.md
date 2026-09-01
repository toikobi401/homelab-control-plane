# Theme Device Hub cho MeshCentral

Làm MeshCentral trông như cùng một ứng dụng với hub, thay vì hai app lồng nhau
khi nhúng ở tab **Điều khiển**.

## Cài

```powershell
.\deploy.ps1                       # chép sang D:\App\MeshCentral
.\deploy.ps1 -MeshCentralPath X:\… # nếu cài chỗ khác
.\deploy.ps1 -Remove               # gỡ, về diện mạo gốc
```

Sau đó **bắt buộc** khởi động lại MeshCentral — nó chốt danh sách file web lúc
khởi động, không đọc lại theo từng request:

```powershell
# PowerShell quyền admin
Restart-Service "meshcentral.exe"
```

Rồi tải lại trang bằng `Ctrl+Shift+R` để bỏ qua cache trình duyệt.

## Cách nó hoạt động

MeshCentral **luôn nạp** `styles/custom.css` và `scripts/custom.js` trong mọi
trang, và `custom.css` là stylesheet **cuối cùng** trong `<head>` — cả hai điều
này đo được bằng trình duyệt thật, không phải suy đoán từ tài liệu.

Nhờ vậy:

- Ta thắng về độ ưu tiên CSS mà gần như không cần `!important`.
- **Không sửa gì trong `node_modules`**, nên `npm update` không xoá mất theme.

File được đặt ở `meshcentral-web/public/` — thư mục override chính thức của
MeshCentral, được ưu tiên hơn `node_modules`.

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

| Phần tử | Trước | Sau |
|---|---|---|
| `#page_leftbar` | `linear-gradient(#104893 → #113962)` | `#171717` |
| `#masthead` | `rgb(0,51,102)` navy | `#171717` |
| `#footer` | `rgb(17,57,98)` | `#171717` |
| `--bs-border-radius` | `0.375rem` | `10px` |
| Font | `system-ui` | Inter |

**Bẫy đã gặp:** sidebar là `#page_leftbar`, **không phải** `#topbar` như tên gợi
ý; và nó dùng `background-image: linear-gradient`, nên đặt `background-color`
không có tác dụng — phải ghi đè `background` (thuộc tính gộp).

## Giới hạn đã biết

- **Icon sidebar là ảnh PNG**, không đổi màu bằng CSS được. Chúng vẫn đọc được
  trên nền tối nên để nguyên.
- **Nút `.btn-primary` của MeshCentral** ở vài chỗ dùng class riêng, chưa phủ
  hết — cần kiểm chứng thêm sau khi restart.
- **Trang đăng nhập** dùng template `login2.handlebars` riêng; CSS đã nhắm tới
  nhưng chưa xem được sau restart.
