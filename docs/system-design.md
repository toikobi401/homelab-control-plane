# System Design — Homelab Control Plane (đến 2026-09-02)

> Ảnh chụp kiến trúc **thực tế đã build**, không phải kế hoạch. Nguồn: `CONTEXT.md` (luật).
> Sơ đồ đầy đủ ở `system-design.html` — mở bằng trình duyệt.

---

## 1. Bức tranh toàn cảnh

Một hub cá nhân điều khiển PC + laptop từ điện thoại/desktop, xem qua trình duyệt. Không app native.
Ba phần:

| Phần | Vai trò | Ngôn ngữ |
|---|---|---|
| **Frontend** | Giao diện web, chạy trong trình duyệt | React + TypeScript |
| **Hub.Api** | Backend duy nhất: xác thực, dữ liệu, proxy nội dung | .NET 10 / ASP.NET Core |
| **MeshCentral** | Server bên thứ ba, tự host, làm mọi việc "chạm vào máy" | Node.js (không phải code của ta) |

**Không có agent tự viết.** Từng có (`Hub.Agent` + `Hub.Windows`), bị xoá 2026-09-01 — xem §4.

## 2. Hai đường vào, một backend

Hệ thống nhận traffic từ hai hướng, hội tụ vào cùng một `Hub.Api`:

- **Tailnet** (ưu tiên khi ở nhà): thiết bị đã cài Tailscale gọi thẳng
  `hub.tailnet-example.ts.net`, không ra Internet.
- **Cloudflare Tunnel** (khi ở ngoài): `cloudflared` chạy trên máy hub, **gọi ra** tới Cloudflare —
  không mở cổng nào trên router. Đây là lý do không cần port forwarding.

Hai đường có **chế độ bind khác nhau** ở `Hub.Api` (`HUB_BIND_MODE`): `Tailnet` bind thẳng địa chỉ
`100.x.y.z`; `Tunnel` chỉ nghe `127.0.0.1`, vì `cloudflared` là bên duy nhất được gọi vào. Sai chế
độ là lỗi bảo mật thật, không phải chi tiết cấu hình — xem CONTEXT.md §4.

## 3. Bốn luồng dữ liệu, bốn cơ chế khác nhau

Đây là phần dễ hiểu lầm nhất nếu chỉ đọc danh sách tính năng — bốn năng lực trông giống nhau ("làm
việc gì đó với thiết bị") nhưng đi bốn đường hoàn toàn khác:

| Năng lực | Đường đi | Vì sao khác |
|---|---|---|
| Đăng nhập, hiện diện, sao lưu | Browser → `Hub.Api` → SQLite | Dữ liệu của riêng hub, không ai khác cần biết |
| Đọc truyện (MangaDex) | Browser → `Hub.Api` (proxy) → MangaDex | Trình duyệt không nói được HTTP/3; `Hub.Api` che luôn |
| **Điều khiển máy, màn hình, file** | Browser → **thẳng tới MeshCentral** (iframe) | `Hub.Api` **không tham gia** — không phải proxy, không thấy traffic |
| MeshCentral ↔ máy đích | MeshCentral ↔ MeshAgent (mỗi máy cài riêng) | Kênh WebSocket độc lập, hub không đứng giữa |

Điểm quan trọng nhất: **`Hub.Api` không đứng giữa việc điều khiển máy.** Trang "Điều khiển" trong
frontend chỉ là một `<iframe>` trỏ thẳng vào MeshCentral — `Hub.Api` không proxy, không thấy, không
ghi log phiên đó. Nếu MeshCentral chết, `Hub.Api` vẫn sống; nếu `Hub.Api` chết, phiên điều khiển đang
mở trong iframe **không bị ngắt** (nhưng không mở phiên mới được, vì trang bọc nó cần đăng nhập).

## 4. MeshCentral thay hẳn agent tự viết

**Quyết định 2026-09-01**, đảo ngược thiết kế ban đầu. Từng có `Hub.Agent` (.NET, tự viết) làm hai
việc: SFTP để duyệt file, và nhận lệnh HTTP để tắt/mở máy. Cả hai chuyển sang MeshCentral vì nó **đã
có sẵn** agent đóng gói cho Windows/Linux/macOS, cộng thêm Wake-on-LAN và điều khiển màn hình —
những thứ dự án chưa làm được.

Hệ quả đo được: `Hub.Core` hết phụ thuộc Windows (đúng hướng chuyển sang NAS), và hub mất hẳn một
loại endpoint có thể đổi trạng thái vật lý máy — bớt một bề mặt tấn công.

## 5. Agent chọn đường: bốc thăm, không phải ưu tiên

Một chi tiết ăn liền vào phần 2: MeshCentral chỉ sinh **một địa chỉ công khai** cho mọi agent. Máy
đã ở trong tailnet vẫn phải nhận cùng cấu hình với máy ở ngoài.

Cách né: script `mesh-agent-route.ps1` **đo kết nối thật** (không tin `tailscale status`) rồi ghi
đúng một địa chỉ vào file cấu hình `.msh` của agent. Khai cả hai địa chỉ tưởng an toàn hơn nhưng
sai: mã nguồn MeshAgent (`agentcore.c`) chọn ngẫu nhiên giữa các địa chỉ đã khai, không ưu tiên
đường gần — máy trong tailnet vẫn đi vòng ra Internet khoảng một nửa số lần.

## 6. Ba năng lực cô lập, cùng một khuôn

Đọc truyện (MangaDex), xem phim (Internet Archive, kế hoạch), và core hub đứng tách biệt có chủ
đích:

- Project `.NET` riêng (`Hub.Manga`, `Hub.Video`), **không phụ thuộc** phần lõi thiết bị/xác thực.
- Backend làm proxy — trình duyệt không gọi thẳng nguồn ngoài (tránh CORS, che vấn đề giao thức).
- Xoá một năng lực này = xoá một project + một thư mục frontend, không rơi rớt ở đâu khác.

## 7. Trạng thái build thật (không phải kế hoạch)

| Phần | Trạng thái |
|---|---|
| `Hub.Api`, `Hub.Core`, `Hub.Data`, `Hub.Manga` | ✅ Build xanh, có test |
| `Hub.Video` | 📝 Chỉ có trong CONTEXT.md §5b, chưa tạo project |
| Xác thực (session cookie + SQLite) | ✅ Chạy thật, đăng xuất-mọi-thiết-bị hoạt động |
| MeshCentral tích hợp | ✅ iframe + theme + định tuyến hai đường, đã kiểm chứng |
| Cloudflare Tunnel | ✅ Code + tài liệu xong |
| Tailscale trên cả 4 thiết bị | 🟡 Mới xong PC |

Chi tiết đầy đủ về các quyết định kiến trúc: `CONTEXT.md`.
