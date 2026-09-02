# Homelab Control Plane

Một control plane tự host (self-hosted) để điều khiển và quản lý các thiết bị cá nhân — PC, laptop,
điện thoại — từ **một giao diện web duy nhất**, không cài app native trên bất kỳ nền tảng nào.

Backend .NET 10 chạy trên máy hub, frontend React chạy trong trình duyệt. Mọi thiết bị truy cập qua
trình duyệt, đi trong mạng riêng WireGuard (Tailscale) hoặc qua Cloudflare Tunnel khi ở ngoài —
**không mở cổng nào trên router**.

> Dự án cá nhân, xây để dùng thật. Tài liệu kiến trúc đầy đủ ở [CONTEXT.md](CONTEXT.md); sơ đồ hệ
> thống ở [docs/system-design.md](docs/system-design.md).

---

## Tech stack

| Lớp | Công nghệ |
|---|---|
| Backend | .NET 10 / ASP.NET Core Minimal API, C# |
| Dữ liệu | EF Core + SQLite |
| Frontend | React + TypeScript, Vite, Tailwind CSS |
| Test | xUnit (backend), Vitest + Testing Library (frontend) |
| Mạng | Tailscale (WireGuard), Cloudflare Tunnel |
| Điều khiển thiết bị | MeshCentral (self-hosted, bên thứ ba) |
| Đóng gói | Docker Compose |

## Kiến trúc

```
                    ┌──────────────────────────────┐
   Trình duyệt ───► │  Hub.Api  (ASP.NET Core)     │ ──► SQLite
   (mọi thiết bị)   │  auth · devices · proxy      │ ──► Tailscale API
                    └──────────────────────────────┘
          │
          └────────────────► MeshCentral (iframe, đi thẳng — Hub.Api không proxy)
                                   │
                                   └──► MeshAgent trên từng máy đích
```

Backend chia project theo trách nhiệm: `Hub.Api` (endpoint + host), `Hub.Core` (domain, không phụ
thuộc hạ tầng), `Hub.Data` (EF Core), `Hub.Manga` (năng lực cô lập). Mỗi năng lực đứng riêng để xoá
được mà không rơi rớt sang phần khác.

## Ba quyết định kỹ thuật đáng chú ý

**1. Web thay vì native — và ghi rõ cái giá phải trả.**
Một web UI chạy trên mọi thứ có trình duyệt; viết app cho cả Android lẫn iOS là nhân đôi công sức
cho một dự án cá nhân. Đánh đổi được ghi thẳng trong tài liệu thay vì giấu đi: mất theo dõi vị trí
nền (đã **bỏ hẳn** năng lực này), mất truy cập file tuỳ ý trên điện thoại, mất push khi đóng tab.

**2. Không tự xây NAT traversal, định danh thiết bị, hay mã hoá transport.**
Mọi thiết bị tham gia một Tailscale tailnet. Đổi lại: vượt NAT không cần port forwarding, xác thực
lẫn nhau, mã hoá đầu-cuối, địa chỉ ổn định — tất cả miễn phí. Cái giá là mọi thiết bị phải cài
Tailscale trước.

**3. Giao việc nặng cho công cụ đã kiểm chứng.**
Điều khiển màn hình, truyền file, Wake-on-LAN đều giao cho MeshCentral thay vì tự viết agent — một
`Hub.Agent` tự viết đã từng tồn tại và bị **xoá bỏ** khi thấy MeshCentral làm tốt hơn. Hệ quả đo
được: `Hub.Core` hết phụ thuộc Windows, và hub mất hẳn một loại endpoint có thể đổi trạng thái vật
lý của máy — bớt một bề mặt tấn công.

Điểm quan trọng trong luồng điều khiển: **`Hub.Api` không đứng giữa**. Trang điều khiển là một
`<iframe>` trỏ thẳng vào MeshCentral — hub không proxy, không thấy, không ghi log phiên đó.

## Bảo mật

- Xác thực tự làm bằng session cookie + SQLite. **Không OAuth, không Firebase/Auth0** — hệ thống
  chạy hoàn toàn nội bộ, không phụ thuộc nhà cung cấp danh tính bên ngoài.
- Backend bind theo chế độ (`HUB_BIND_MODE`): chế độ `Tailnet` bind đúng địa chỉ tailnet; chế độ
  `Tunnel` chỉ nghe `127.0.0.1` vì `cloudflared` là bên duy nhất được phép gọi vào. Bind sai là lỗi
  bảo mật thật, không phải chi tiết cấu hình.
- Secret đọc từ biến môi trường và .NET user-secrets, không nằm trong mã nguồn. Xem
  [.env.example](.env.example).
- Hỗ trợ đăng xuất mọi thiết bị (thu hồi toàn bộ session đang mở).

> Mọi địa chỉ IP, hostname và tên tailnet trong repo này là **giá trị ví dụ**
> (`100.100.100.100`, `hub.tailnet-example.ts.net`), không phải hạ tầng thật.

## Chạy thử

Yêu cầu: .NET 10 SDK, Node.js 20+, Docker (tuỳ chọn).

```bash
cp .env.example .env          # điền HUB_TAILNET_IP bằng: tailscale ip -4

# Backend
dotnet run --project backend/Hub.Api

# Frontend (terminal khác)
cd frontend && npm install && npm run dev
```

Hoặc chạy toàn bộ bằng Docker:

```bash
docker compose up -d
```

## Test

```bash
dotnet test Hub.sln           # xUnit — backend
cd frontend && npm test       # Vitest — frontend
```

## Cấu trúc thư mục

```
backend/     Hub.Api · Hub.Core · Hub.Data · Hub.Manga (+ project test)
frontend/    React + TypeScript, tổ chức theo feature
docs/        Sơ đồ hệ thống, hướng dẫn cài MeshCentral và truy cập Internet
scripts/     Tiện ích Python/PowerShell cho việc đăng ký và định tuyến agent
```

## Giấy phép

[MIT](LICENSE)
