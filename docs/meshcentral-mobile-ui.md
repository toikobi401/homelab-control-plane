# Điều tra: UI điều khiển khó dùng trên điện thoại

Ba lỗi tìm được khi đo giao diện MeshCentral ở khổ 390×844. Hai lỗi đầu **làm mất chức năng**
— có những nút không cách nào bấm được. Lỗi thứ ba hoá ra không phải lỗi của MeshCentral.

Mọi con số dưới đây **đo trên trang thật đang chạy**, không ước lượng.

| # | Triệu chứng | Nguyên nhân gốc | Commit |
|---|---|---|---|
| 1 | 4 trong 9 tab thiết bị không mở được | Con tràn `overflow-x: visible`, cha `hidden` | `be826cf` |
| 2 | Chữ trạng thái và nút Type bị cắt | `.d-flex` con cứng `nowrap` vô hiệu hoá `flex-wrap` của cha | `82059a2` |
| 3 | Màn hình từ xa chỉ cao 82px | Không phải lỗi — nút sửa có sẵn nhưng nằm ngoài màn hình | `7a0755d` |

## Bối cảnh: hai lớp giao diện

Cần phân biệt rõ, vì sửa nhầm lớp thì không có tác dụng gì:

```
hub React app  ─ RemotePage.tsx ─ toolbar + iframe   ← Tailwind, sửa trong frontend/
    └── iframe
          └── MeshCentral  ─ default3.handlebars     ← Bootstrap 5, sửa bằng custom.css
```

Cả ba lỗi nằm **bên trong iframe**, nên đều sửa bằng `meshcentral-theme/public/styles/custom.css`.
MeshCentral luôn nạp `custom.css` và đặt nó **cuối cùng** trong `<head>`, nên nó thắng độ ưu tiên
mà phần lớn trường hợp không cần `!important`.

Lớp vỏ hub ăn mất **220px (26%)** của màn hình 390×844 trước khi MeshCentral có được chỗ nào —
header 57px, toolbar, padding, thanh điều hướng dưới 57px. Đây là bối cảnh cho cả ba lỗi: khung
iframe chỉ còn rộng **290px**.

---

## Lỗi 1 — Bốn tab thiết bị không cách nào mở được

### Đo được

```
#topbar      scrollWidth 535px · clientWidth 320px · overflow-x: visible
#container   (cha)                                 · overflow-x: hidden
```

### Nguyên nhân gốc

Cha cắt phần tràn, con không tự cuộn được. Chín tab cần 535px nhưng chỉ 320px hiển thị được, và
**không có cơ chế nào chạm tới 215px còn lại**.

Đây là kiểu lỗi nguy hiểm vì trông giống lỗi thẩm mỹ. Thực tế bốn tab **Software, Events,
Details, Console** biến mất hoàn toàn khỏi giao diện điện thoại.

### Cách sửa

Cho `#topbar` tự cuộn ngang thay vì trông chờ cha:

```css
@media (max-width: 900px) {
  #topbar {
    overflow-x: auto;
    -webkit-overflow-scrolling: touch;
  }
  #topbar > * { white-space: nowrap; }
}
```

### Kiểm chứng

`maxScrollLeft` = 225px, cuộn tới cuối thì cả chín tab đều bấm được.

---

## Lỗi 2 — Thanh công cụ remote desktop cắt mất nút

### Đo được

```
#deskarea1 (.areaHead)  scrollWidth 307 > clientWidth 290
#deskarea4 (.areaFoot)  scrollWidth 402 > clientWidth 290
```

Hậu quả: chữ trạng thái ("Disconnected") và nút **Type** bị cắt.

### Nguyên nhân gốc

Chỗ này có một cái bẫy. Cả hai vùng **đã có `flex-wrap: wrap`** — nhìn CSS thì tưởng đã xử lý rồi.
Nhưng con trực tiếp bên trong là một `.d-flex` với `flex-wrap: nowrap` rộng 402px:

```
#deskarea4          flex-wrap: wrap     ← muốn xuống dòng
  └── .d-flex       flex-wrap: nowrap   ← nhưng con là MỘT KHỐI CỨNG 402px
```

`flex-wrap` của cha chỉ xuống dòng được **giữa các con**. Khi chỉ có một con và bản thân nó không
chịu xuống dòng, cha không có chỗ nào để ngắt. Wrap có mà vô dụng.

### Cách sửa

Ép chính `.d-flex` con đó wrap, cộng thêm cuộn ngang làm lưới an toàn:

```css
@media (max-width: 900px) {
  #deskarea1, #deskarea4 { overflow-x: auto; }
  #deskarea1 > .d-flex,
  #deskarea4 > .d-flex { flex-wrap: wrap !important; }
}
```

`!important` ở đây là cần thiết — đang ghi đè utility class của Bootstrap.

### Kiểm chứng

Cả hai: `scrollWidth == clientWidth == 290`, không còn gì bị cắt.

---

## Lỗi 3 — Màn hình từ xa chỉ cao 82px

Đây là lỗi tôi báo cáo sai lúc đầu. Ghi lại cả đường đi vì kết luận đầu tiên nghe rất hợp lý.

### Giả thuyết ban đầu — và nó sai

Đo chiều cao bên trong iframe:

```
masthead 66 + tabs 24 + #p11title 97 + #deskarea1 106 + #deskarea4 124 = 417px chrome
canvas #Desk                                                          = 145px
```

417px khung viền cho 145px nội dung. Kết luận tự nhiên: **cắt bớt chrome thì canvas to ra**.

Tôi thu `#p11title` từ 97px xuống 34px, giải phóng 63px. **Canvas không nhúc nhích** — vẫn đúng
145px. Giả thuyết sai.

Đo kỹ hơn mới thấy vì sao:

```json
canvas:  { cssH: "145px", inlineStyle: "height: 145px; width: 100%;" }
parent:  { id: "DeskParent", h: 620, inlineStyle: "overflow: hidden;" }
connectionState: "Disconnected"
```

`#DeskParent` **đã có sẵn 620px**. Chỗ trống chưa bao giờ là vấn đề. 145px là **inline style do
JavaScript đặt**, nên giải phóng thêm không gian chẳng thay đổi gì.

### Kết nối thật rồi đo lại

Vì đang ở trạng thái "Disconnected", tôi kết nối thật để loại trừ khả năng đó là hiện tượng của
trạng thái ngắt kết nối:

```
Connected · inline: "height: 81.5625px; width: 100%;"
canvas.width = 3840 · canvas.height = 1080
```

Kết nối xong canvas còn **nhỏ hơn** — 82px. Và đây là mấu chốt: máy thật chạy **3840×1080**, tức
**hai màn hình ghép ngang**, tỉ lệ 3.56:1.

### Nguyên nhân gốc

Đọc `deskAdjust()` trong `default3.handlebars:11658`, có ba chế độ theo biến `deskAspectRatio`:

| Giá trị | Chế độ | Hành vi |
|---|---|---|
| `0` (mặc định) | Fixed | Giữ tỉ lệ: `height = deskH × parentW / deskW` |
| `1` | Zoomed | 1:1, `DeskParent` cho cuộn hai chiều |
| `2` | Scale | `width/height: 100%`, kéo giãn méo hình |

Thay số vào chế độ 0:

```
1080 × 290 / 3840 = 81.5625px
```

Đúng **đến từng chữ số thập phân** với inline style đo được.

**MeshCentral tính không sai.** 82px là câu trả lời đúng cho một câu hỏi sai — "làm sao vừa chiều
rộng 290px" — trong khi trên điện thoại người ta cần "làm sao đọc được chữ".

### Chức năng giải quyết việc đó đã có sẵn

Chế độ 1 (Zoomed) chính là cách dùng remote desktop trên màn hình nhỏ: canvas ở kích thước thật,
chữ đọc được, lướt ngón tay để đi quanh màn hình. Nút chuyển chế độ **đã tồn tại**, và
`toggleAspectRatio()` còn `putstore` để nhớ lựa chọn giữa các phiên.

Thử cả ba chế độ trên trang thật:

| Chế độ | Canvas | `DeskParent` overflow |
|---|---|---|
| 0 Fixed | 290 × 82 | hidden |
| 1 Zoomed | **3840 × 1080** | **scroll** |
| 2 Scale | 290 × 620 | hidden |

Cả ba đều chạy đúng. Chế độ 2 lấp đầy màn hình nhưng bóp 3.56:1 xuống 0.47:1 — chữ sẽ nát.

### Vấn đề thật: không ai tìm thấy nút đó

```
title: "Toggle View Mode" · text: "⇲"
rect: { x: 355, w: 16, h: 31 }     ← khung iframe rộng 290px
```

Nút nằm **ngoài vùng nhìn thấy 65px**, và vùng chạm rộng 16px — quá nhỏ so với ngưỡng 44px cho
đầu ngón tay.

Thủ phạm là `.ms-auto` (Bootstrap `margin-left: auto`) đẩy cả nhóm nút sang mép phải. Trên desktop
thì đẹp; trên 290px thì đẩy hẳn ra ngoài.

Đáng nói: lỗi này **chỉ với tới được sau khi sửa lỗi 2**. Trước đó `#deskarea1` không cuộn, nên nút
hoàn toàn không tồn tại với người dùng điện thoại.

### Cách sửa

```css
@media (max-width: 900px) {
  #deskarea1 .ms-auto { margin-left: 0 !important; }

  #deskarea1 .deskareaicon,
  #deskarea4 .deskareaicon {
    display: inline-flex !important;
    min-width: 44px;
    min-height: 44px;
  }

  #p11title .fs-4 { font-size: 0.95rem !important; white-space: nowrap; /* … */ }
  #p11title .deviceGroupName { display: none; }
}
```

**Không ghi đè chiều cao canvas bằng CSS.** Đó là inline style do JS đặt; ép nó sẽ phá tỉ lệ mà
`deskAdjust()` đang giữ đúng.

### Kiểm chứng

| | Trước | Sau |
|---|---|---|
| Nút đổi chế độ | `x:355 w:16` — ngoài màn hình 65px | `x:137 w:44 h:44` — trong màn hình |
| `#p11title` | 97px (3 dòng) | **34px** |
| `#deskarea1` | 307 > 290 tràn | 290 = 290 |
| `#deskarea4` | 402 > 290 tràn | 290 = 290 |
| Tiêu đề | "Desktop - War_Machine_2 - May nha" | "Desktop - War_Machine_2" |

Nút Back còn nguyên.

---

## Cái bẫy: ETag khớp không có nghĩa là CSS đã có tác dụng

Lần đo đầu sau khi deploy cho kết quả **y hệt lúc chưa sửa** — kể cả khối CSS của lỗi 2 đã commit
từ trước đó.

Trong khi ETag lại xác nhận server đang phục vụ bản mới:

```
ETag 6da4 = 28068 byte  ✓ khớp đúng kích thước file trong repo
```

Nguyên nhân là hai đường tải khác nhau:

| Đường | Cache | Thấy gì |
|---|---|---|
| `<link href="/styles/custom.css">` không query | Lấy từ **cache đĩa** | Bản **cũ** |
| `fetch(url, {cache:'reload'})` để kiểm ETag | **Đi vòng** qua cache | Bản mới |

Bằng chứng dứt khoát — so nội dung server với stylesheet trình duyệt đang thật sự dùng:

```
server:      28068 byte, có `deskareaicon`  ✓
stylesheet:  42 quy tắc, KHÔNG có `deskareaicon`  ✗
```

Sau khi ép nạp lại `link.href += '?v=' + Date.now()`: **45 quy tắc**, có `deskareaicon`.

> **Hệ quả:** ETag khớp **không** đủ để kết luận CSS đang có tác dụng. Nếu chỉ nhìn ETag thì đã
> báo "xong" trong khi trang thật chưa đổi gì. `Ctrl+Shift+R` là **bắt buộc**, không phải khuyến nghị.

Cách kiểm chứng đúng — đọc chính stylesheet trình duyệt đang dùng, không phải fetch riêng:

```js
[...document.styleSheets]
  .find(s => /custom\.css/.test(s.href || ''))
  .cssRules.length
```

## Triển khai

`meshcentral-theme/deploy.ps1` chép vào **cả hai đích** — chi tiết vì sao cần cả hai nằm trong
comment đầu file đó. Sau khi chép:

1. Khởi động lại MeshCentral — **chỉ cần khi thêm file mới**. Ghi đè file đã có thì đọc lại được ngay.
2. `Ctrl+Shift+R` — **luôn luôn cần**, xem phần cái bẫy ở trên.

## Còn lại

Hai việc chưa làm, không nằm trong phạm vi "sửa từng lỗi đã phát hiện":

**Nút vẫn là "⇲" và không cho biết đang ở chế độ nào.** Với tới được rồi nhưng vẫn khó đoán —
người dùng phải bấm thử mới biết. Sửa được bằng `custom.js` (thêm nhãn chữ), nhưng đó là thay đổi
hành vi chứ không phải bố cục.

**`views/default-mobile.handlebars` tồn tại nhưng không được dùng.** `docs/meshcentral-setup.md`
đã ghi nhận view mobile riêng 8.372 dòng. Có thể là đường đi tốt hơn hẳn so với vá CSS, nhưng cần
thử để biết nó có hỗ trợ remote desktop hay chỉ là bản rút gọn.

## Tham chiếu

| Thứ | Chỗ |
|---|---|
| CSS đã sửa | `meshcentral-theme/public/styles/custom.css:653` (lỗi 1), `:704` (lỗi 2), `:753` (lỗi 3) |
| `deskAdjust()` | `node_modules/meshcentral/views/default3.handlebars:11658` |
| `toggleAspectRatio()` | cùng file, `:2923` |
| Lớp vỏ hub | `frontend/src/features/remote/RemotePage.tsx` |
| Script triển khai | `meshcentral-theme/deploy.ps1` |
