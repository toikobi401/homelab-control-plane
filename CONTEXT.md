# CONTEXT.md — Homelab Control Plane (Trung tâm điều khiển thiết bị cá nhân)

> **Đọc file này trước khi làm bất cứ việc gì.** Nếu một yêu cầu mâu thuẫn với quy tắc ở đây, hãy dừng
> lại và hỏi thay vì tự quyết định. Nếu một quyết định trong file này hoá ra là sai, hãy đề xuất chỉnh
> sửa chính file này như một phần của thay đổi — không được lặng lẽ né tránh nó.

---

## 1. Dự án này là gì

Một **hub thiết bị cá nhân, tự host (self-hosted)** dành cho các thiết bị của riêng một người dùng.
Không phải sản phẩm thương mại, không đa người dùng, không bán cho ai.

Hub kết nối: 1 điện thoại Android, 1 iPhone, 1 máy tính để bàn Windows, 1 laptop Windows, và về sau
là một NAS cá nhân.

**Hệ thống là một ứng dụng web.** Backend .NET chạy trên PC, frontend React chạy trong trình duyệt.
Mọi thiết bị — iPhone, Android, desktop — đều truy cập bằng trình duyệt qua một miền nội bộ. Không
có ứng dụng native nào cả.

Năm năng lực, theo thứ tự xây dựng:

| # | Năng lực | Trạng thái |
|---|---|---|
| 1 | Sổ đăng ký thiết bị + hiện diện (online/offline, lần cuối thấy) | ✅ Xong — đọc từ Tailscale |
| 2 | Duyệt và truyền file giữa các thiết bị | **Giao cho MeshCentral** — không tự xây (§2.3) |
| 3 | Sao lưu lên cloud storage, sau đó lên NAS cá nhân | Chưa bắt đầu |
| 4 | Điều khiển màn hình từ xa vào PC/laptop Windows | **Giao cho MeshCentral** |
| 5 | Đọc truyện tranh qua API công khai (MangaDex, …) | Chưa bắt đầu |
| 6 | Tắt, mở, khởi động lại máy từ xa (đánh thức qua waker — §5a.1) | **Giao cho MeshCentral** |
| 7 | Xem phim từ kho công cộng (Internet Archive) | Chưa bắt đầu |

Các năng lực 2, 3, 4, 6 phụ thuộc vào lớp transport và xác thực của năng lực 1. Không làm sai thứ tự.

**Năng lực 5 là ngoại lệ và đứng riêng** — xem §5. Nó không dùng tailnet và không phụ thuộc năng lực
1. Nằm cuối hàng đợi.

### Những thứ dứt khoát không làm (non-goals)

- **Không ứng dụng native.** Không Android app, không iOS app, không desktop app. Chỉ web. Đây là lý
  do tồn tại của cả kiến trúc này — xem §2.
- Không đa người dùng, không đăng ký, không mời người khác vào. Một người dùng, vài thiết bị.
- **Không dùng OAuth, không Google/Apple/GitHub sign-in, không Auth0/Firebase/Clerk.** Xác thực tự
  làm, chạy hoàn toàn nội bộ — xem §6.
- Không dùng backend cloud mà người dùng phải trả tiền hoặc phải vận hành. (Ngoại lệ duy nhất: năng
  lực 5 gọi API công khai của bên thứ ba — xem §5.)
- ~~Không đưa hệ thống ra Internet công cộng.~~ **Đã đổi 2026-08-31** — xem §4a. Vẫn **không port
  forwarding**; ra Internet qua Cloudflare Tunnel, và tailnet vẫn là đường ưu tiên.
- **Không theo dõi vị trí.** Đã bỏ hẳn — xem §2.

---

## 2. Hai quyết định kiến trúc quan trọng nhất

### 2.1. Web thay vì native

**Lý do: khả năng tương thích.** Một ứng dụng Android chỉ chạy trên Android. Người dùng có cả
iPhone, và viết thêm app iOS là nhân đôi công sức cho một dự án cá nhân — chưa kể cần máy Mac và tài
khoản Apple Developer trả phí hằng năm chỉ để cài lên máy của chính mình.

Một web UI chạy trên mọi thứ có trình duyệt. Đây là đánh đổi có ý thức, và cái giá phải trả được ghi
thẳng ở đây thay vì giấu đi:

| Mất gì | Hệ quả thật |
|---|---|
| **Theo dõi vị trí nền** | **Đã bỏ năng lực này hoàn toàn.** Trình duyệt không chạy nền được; đóng tab là dừng. Không có cách vòng nào tử tế. |
| Thông báo đẩy khi đóng tab | Web Push có làm được nhưng phiền; iOS còn bắt thêm bước cài về màn hình chính. Chưa làm. |
| Truy cập file tuỳ ý trên điện thoại | Trình duyệt chỉ thấy file người dùng tự chọn. Năng lực 2 vì thế **chỉ áp dụng cho PC và laptop** (nơi có agent), không duyệt được file trong điện thoại. Nay việc này do MeshCentral làm — giới hạn vẫn nguyên, chỉ đổi người thực hiện. |
| Chạy khi máy khởi động | Chỉ backend .NET và agent chạy nền được. Trình duyệt thì không. |

Ai đọc file này về sau và thấy tiếc những thứ trên: **đừng đề xuất quay lại native.** Quyết định đã
cân nhắc, và tương thích đa thiết bị quan trọng hơn.

### 2.2. Không tự xây NAT traversal, định danh thiết bị, hay mã hoá transport

Tất cả thiết bị tham gia một **Tailscale tailnet** (lưới WireGuard) — bao gồm cả iPhone và Android,
vì cả hai đều có ứng dụng Tailscale chính thức. Mỗi thiết bị nhận một địa chỉ `100.x.y.z` ổn định.

Điều này cho ta miễn phí: vượt NAT/firewall không cần port forwarding, xác thực lẫn nhau, mã hoá
đầu-cuối, và địa chỉ ổn định.

Mọi thứ ta xây đều giả định **"tất cả thiết bị của tôi nằm trên cùng một mạng LAN phẳng, tin cậy, đã
mã hoá."**

Nếu Tailscale trở nên không chấp nhận được, phương án dự phòng là tự host Headscale (cùng client,
control plane riêng). Không đề xuất cấu hình WireGuard thuần, hole-punching tự viết, hay relay server
công khai.

**Hệ quả bắt buộc:** mọi thiết bị muốn dùng hệ thống đều phải cài Tailscale trước. Đây là cái giá để
đổi lấy việc không phải mở cổng ra Internet. Chấp nhận nó.

### 2.3. Hệ quả chung: tái sử dụng giao thức, đừng phát minh lại

| Nhu cầu | Dùng | KHÔNG được |
|---|---|---|
| Duyệt và truyền file giữa desktop | **MeshCentral** (đã có sẵn trong agent) | Viết giao thức truyền file riêng, hay dựng trang duyệt file của ta |
| Đồng bộ file (về sau) | Syncthing, điều khiển bởi UI của ta | Tự viết lại đồng bộ mức block |
| Sao lưu cloud/NAS | Gọi binary `rclone` từ backend | Tự tay viết client S3/Drive/WebDAV |
| Điều khiển màn hình | **MeshCentral** (thay cho noVNC ghi ban đầu) | Viết codec hay giao thức nhập liệu |
| Điều khiển nguồn, Wake-on-LAN | **MeshCentral** | Tự viết agent nhận lệnh tắt/mở máy |

Hệ thống của ta là một **control plane và một UI**. Phần việc nặng giao cho công cụ đã được kiểm
chứng. Bất kỳ PR nào bắt đầu viết lại một mục ở cột bên phải đều bị từ chối.

---

## 3. Stack — đã chốt, không bàn lại

### Backend (.NET)

- **.NET 10 LTS**, **ASP.NET Core**. C#. Ghim phiên bản SDK bằng `global.json` để mọi máy build
  giống nhau.
- **Minimal API** cho các endpoint. Không MVC controller trừ khi có lý do rõ ràng.
- **EF Core + SQLite** cho lưu trữ. Một file DB, không cần server DB riêng.
- **`System.Text.Json`** cho JSON. Không Newtonsoft.
- **`HttpClient` qua `IHttpClientFactory`.** Không tạo `HttpClient` mới mỗi request — lỗi kinh điển
  làm cạn socket.
- **Serilog** cho log, ghi ra file có xoay vòng. Áp dụng quy tắc "không log thứ nhạy cảm" ở §6.
- Chạy như **Windows Service** (`Microsoft.Extensions.Hosting.WindowsServices`), khởi động cùng máy.
- Thiết kế để **sau này chuyển sang NAS** — xem §3.3.

### Frontend (React)

- **React 18+, TypeScript**, `strict: true`. Không JavaScript thuần.
- **Vite** làm build tool. Không CRA (đã ngừng phát triển), không Next.js (ta không cần SSR, không
  cần một server thứ hai).
- **TanStack Query** cho mọi lời gọi API — cache, retry, trạng thái loading/error miễn phí. Không tự
  viết `useEffect` + `fetch` rải rác.
- **React Router** cho điều hướng.
- **shadcn/ui + Tailwind CSS** cho giao diện. Lưu ý cách nó hoạt động: shadcn/ui **không phải một
  dependency npm** — nó chép mã nguồn component thẳng vào repo (`/src/components/ui`). Nghĩa là ta
  sở hữu và sửa được mã đó, và nó không phình theo phiên bản.
- Hệ quả: **component trong `/src/components/ui` được phép sửa**, khác với thư viện thường. Nhưng
  sửa có chừng mực — chép lại từ upstream khi cần cập nhật sẽ đè lên thay đổi của ta.
- **`lucide-react`** cho icon (đi kèm shadcn/ui). Không thêm bộ icon thứ hai.
- Không thêm thư viện UI nào khác. Không MUI, không Ant Design, không Bootstrap. Trộn nhiều hệ thống
  thiết kế là con đường nhanh nhất tới giao diện lộn xộn.
- **Mobile-first.** Giao diện chính sẽ dùng trên điện thoại — thiết kế cho màn hình hẹp trước, rộng
  sau. Nút bấm đủ lớn cho ngón tay.
- Frontend được build ra file tĩnh và **do chính backend .NET phục vụ**. Không dựng web server thứ
  hai (nginx/IIS) chỉ để phục vụ file tĩnh.

### Agent trên máy desktop — đã bỏ (2026-09-01)

**Hub không còn agent tự viết.** `Hub.Agent` và `Hub.Windows` đã bị xoá khỏi solution.

Agent tự viết ra đời để phục vụ năng lực 2 (SFTP) và năng lực 6 (điều khiển nguồn). Cả hai nay
giao cho **MeshCentral**, thứ đã có agent đóng gói sẵn cho Windows/Linux/macOS làm đủ những việc
đó cộng thêm Wake-on-LAN và điều khiển màn hình. Giữ agent riêng là bảo trì hai thứ cùng làm một
việc — đúng điều §2.3 cấm.

Hệ quả kèm theo, đều là điều tốt:

- `Hub.Core` không còn phụ thuộc gì vào Windows — hợp §3.3 (chuẩn bị chuyển sang NAS).
- Bớt một đường tấn công: hub không còn endpoint nào đổi trạng thái vật lý của máy.

**Nếu sau này cần thông tin máy mà MeshCentral không cấp** (ví dụ nhãn LAN cho một năng lực mới):
cân nhắc đọc từ API của MeshCentral trước, chỉ dựng agent riêng khi đã chắc là không có đường nào
khác.

### 3.3. Chuẩn bị cho việc chuyển sang NAS

Backend tạm chạy trên PC, nhưng đích đến là NAS chạy 24/7. Vì vậy, **ngay từ đầu**:

- **Không dùng API chỉ có trên Windows trong phần lõi.** Registry, WMI, P/Invoke Win32 — nếu cần,
  đặt sau một interface và đưa hiện thực vào lớp riêng cho Windows.
- **Không hardcode đường dẫn Windows.** Dùng `Path.Combine`, đọc từ cấu hình. Không có ổ đĩa cứng
  nào nằm trong source.
- **Mọi cấu hình đọc từ biến môi trường hoặc `appsettings.json`.** Không cấu hình biên dịch cứng.
- **Đóng gói được bằng Docker.** Chưa cần dựng ngay, nhưng đừng làm gì khiến sau này không đóng gói
  nổi.
- SQLite là lựa chọn đúng cho hướng này — một file, chuyển máy chỉ là chép file.

Riêng năng lực 6 (điều khiển nguồn, chạy lệnh) và năng lực 4 (điều khiển màn hình) **buộc phải** gọi
API Windows. Chúng nằm ở **agent**, không nằm ở backend. Backend chỉ ra lệnh cho agent. Ranh giới này
là thứ cho phép backend chuyển sang NAS mà không phải viết lại.

### Bố cục repo

```
/backend                Solution .NET
  /Hub.Api                ASP.NET Core: endpoint, xác thực, phục vụ file tĩnh
  /Hub.Core               Logic nghiệp vụ, model, interface — KHÔNG phụ thuộc Windows
  /Hub.Data               EF Core, SQLite, migration
  /Hub.Manga              Client MangaDex — cô lập, xem §5
  /Hub.Video              Client Internet Archive — cô lập, xem §5b
/frontend               Ứng dụng React + Vite
  /src/features/devices   Danh sách thiết bị, trạng thái hiện diện
  /src/features/backup    Job sao lưu và lịch sử
  /src/features/remote    MeshCentral nhúng — điều khiển máy, màn hình, file
  /src/features/manga     Đọc truyện (năng lực 5, cô lập)
  /src/features/video     Xem phim (năng lực 7, cô lập)
  /src/components/ui      shadcn/ui — mã chép vào repo, được phép sửa
  /src/shared             Component dùng chung, client API, kiểu dữ liệu
/docs                   Tài liệu nghiên cứu (manga-api-research.md, …)
```

**Luật phụ thuộc:** `Hub.Core` không phụ thuộc vào gì cả (không Windows, không EF, không ASP.NET).
`Hub.Api` phụ thuộc Core/Data. Không có chiều ngược lại.

### Kiểu dữ liệu dùng chung giữa .NET và TypeScript

Backend và frontend là hai ngôn ngữ, nên DTO có nguy cơ lệch nhau. Bắt buộc:

- Backend phơi ra **OpenAPI spec**, frontend **sinh kiểu TypeScript từ spec đó**. Không chép tay.
- Việc sinh kiểu chạy như một script npm, kết quả được commit vào repo để thấy được thay đổi khi
  giao thức đổi.

---

## 4. Miền nội bộ và HTTPS

### Truy cập qua miền nội bộ, không qua địa chỉ IP

Người dùng gõ một tên miền dễ nhớ, ví dụ `hub.internal`, chứ không phải `http://100.x.y.z:5000`.

**Cách làm — dùng Tailscale, đừng tự dựng DNS server:**

- Tailscale có sẵn **MagicDNS**: mỗi máy tự có tên dạng `<tên-máy>.<tailnet>.ts.net`, hoạt động ngay
  trên mọi thiết bị đã cài Tailscale, kể cả iPhone. Đây là lựa chọn mặc định.
- **Không dựng DNS server riêng** (Pi-hole, dnsmasq, bind) chỉ để có một cái tên. Không sửa file
  `hosts` trên từng thiết bị — iPhone không sửa được, và cách đó không mở rộng nổi.

### HTTPS là bắt buộc, không phải tuỳ chọn

Không phải vì sợ nghe lén — tailnet đã mã hoá rồi. Mà vì **trình duyệt khoá nhiều thứ khi không có
HTTPS**: `SubtleCrypto`, service worker, clipboard API, và cảnh báo mật khẩu trên form đăng nhập.

- Dùng **Tailscale HTTPS** (`tailscale cert`) — cấp chứng chỉ Let's Encrypt hợp lệ cho tên
  `.ts.net`, tự động gia hạn. Trình duyệt tin ngay, **không hiện cảnh báo**.
- **Không dùng chứng chỉ tự ký.** iOS bắt cài profile thủ công và rất phiền.
- **Không mở HTTP thường** ngoài việc chuyển hướng sang HTTPS.

### Backend chỉ lắng nghe trên tailnet

Backend **phải** bind vào địa chỉ tailnet (`100.x.y.z`), **không** bind `0.0.0.0`.

Bind `0.0.0.0` là phơi toàn bộ hệ thống ra mạng Wi-Fi nhà — bao gồm cả thiết bị của khách. Với một hệ
thống có quyền chạy lệnh từ xa (§5a), đó là lỗi nghiêm trọng, không phải sơ suất nhỏ.

Khởi động phải **thất bại rõ ràng** nếu không tìm thấy địa chỉ tailnet, thay vì âm thầm tụt về
`0.0.0.0`.

#### Ngoại lệ khi chạy trong container

Trong container, quy tắc trên **đổi chỗ thực thi**, không đổi bản chất. Container có network
namespace riêng, nên `0.0.0.0` bên trong nghĩa là "mọi địa chỉ của container", không phải mọi card
mạng của máy thật. Vì vậy:

- Tiến trình trong container bind `0.0.0.0:8080` — đây là cách duy nhất Docker định tuyến vào được.
- **Ranh giới bảo vệ chuyển ra cổng publish**, và nó bắt buộc gắn địa chỉ tailnet:

  ```yaml
  ports:
    - "${HUB_TAILNET_IP}:5000:8080"   # ĐÚNG
    - "5000:8080"                      # SAI — phơi ra toàn bộ Wi-Fi nhà
  ```

- `compose.yaml` khai `HUB_TAILNET_IP` là biến **bắt buộc**: thiếu thì compose từ chối chạy, không
  âm thầm publish ra mọi địa chỉ.

Chế độ bind là tường minh qua `HUB_BIND_MODE` (`Localhost` / `Tailnet` / `Container`) — xem
`Hub.Api/Hosting/NetworkBinding.cs`. Không có chế độ nào tự ý rơi về `0.0.0.0` ngoài container.

**Cách kiểm chứng** (làm lại mỗi khi đụng vào phần mạng) — từ một máy trong Wi-Fi nhà nhưng
không cài Tailscale:

```
curl http://<IP-LAN-của-máy-chạy-hub>:5000/health   # phải KHÔNG kết nối được
curl http://<IP-tailnet>:5000/health                # phải trả 200
```

---
## 4a. Mở ra Internet — ngoại lệ có điều kiện

**Quyết định 2026-08-31.** §1 ban đầu cấm đưa hệ thống ra Internet, và §4 quy định chỉ bind tailnet.
Người dùng đổi yêu cầu: muốn vào được từ mọi thiết bị, không cần cài Tailscale.

Đây là **đánh đổi có ý thức**, ghi lại đầy đủ để sau này không phải tranh luận lại.

### Mất gì

Tailnet vốn che ba thứ, mở ra Internet là mất cả ba:

| Tailnet che | Ra Internet thì |
|---|---|
| Chỉ thiết bị đã cài Tailscale gọi tới được | Mọi bot quét cổng chạm tới màn hình đăng nhập |
| Không ai dội request | Cần rate limit, nếu không một script là làm nghẽn máy |
| Không có kẻ lạ ở giữa | Cần HSTS và header bảo mật |

Đáng lo hơn bình thường vì hệ thống này **tắt máy và điều khiển màn hình từ xa** (§5a). Một phiên bị
chiếm không phải mất dữ liệu — là mất cả PC lẫn laptop.

### Vẫn giữ

- **Không port forwarding.** Router không mở cổng nào. Cloudflare Tunnel gọi ra, không ai gọi vào.
- **Tailnet vẫn là đường ưu tiên.** Ở nhà thì dùng `hub.tailnet-example.ts.net:7189` — nhanh
  hơn và không qua bên thứ ba.
- **§6 không đổi.** Vẫn một người dùng, session cookie, phiên lưu trong DB, thu hồi được.
- **§5a không đổi.** Vẫn cấm chạy lệnh tuỳ ý, vẫn duyệt thủ công thiết bị, vẫn nhật ký kiểm toán.

### Phải có trước khi mở

Những thứ tailnet vốn che, giờ phải tự làm:

1. **Rate limit** (`Hub.Api/Security/RateLimiting.cs`) — 300 request/phút chung, **10/phút** cho
   endpoint xác thực. Phân vùng theo `CF-Connecting-IP` (Cloudflare đặt, ghi đè giá trị client gửi),
   **không** theo `X-Forwarded-For` vì client tự đặt được.
2. **Header bảo mật** (`Hub.Api/Security/SecurityHeaders.cs`) — HSTS, `X-Frame-Options: DENY`,
   `nosniff`, `Referrer-Policy: no-referrer`.
3. **Chế độ bind `Tunnel`** — chỉ nghe loopback `127.0.0.1:7190`. Không cổng nào mở ra LAN hay
   Internet kể cả khi firewall bị tắt nhầm; chỉ `cloudflared` trên chính máy đó gọi vào được.

### Cái giá phải chấp nhận

**Cloudflare thấy được lưu lượng đã giải mã.** TLS kết thúc ở biên của họ. Với hệ thống điều khiển
máy cá nhân thì đây là đánh đổi thật, không phải chi tiết nhỏ — nhưng đổi lại được chống DDoS, WAF,
và không phải mở cổng nào trên router.

Nếu điều này không chấp nhận được, phương án thay thế là VPS tự quản làm reverse proxy nối về nhà
qua tailnet — tốn tiền hàng tháng và thêm một máy phải vận hành.

---

## 5. Năng lực 5 — Đọc truyện tranh (đứng riêng)

### Vì sao nó đứng riêng

Các năng lực khác đều nói về **thiết bị của tôi nói chuyện với nhau qua tailnet**. Năng lực này thì
khác hẳn: nó là một **client đọc nội dung công khai từ Internet**. Không tailnet, không agent, không
định danh thiết bị.

Ta thừa nhận thẳng thắn: đây gần như là hệ thống thứ hai sống chung một codebase. Chấp nhận điều đó,
và xử lý bằng cách **cô lập nó triệt để** thay vì giả vờ nó ăn khớp với phần còn lại.

Quy tắc cô lập, bắt buộc:

- Toàn bộ code backend nằm trong `Hub.Manga`, frontend nằm trong `/src/features/manga`.
- **`Hub.Manga` không được phụ thuộc vào bất cứ project nào khác ngoài `Hub.Core`.** Nó chỉ gọi ra Internet.
- **Không** dùng chung `HttpClient` đã cấu hình với phần tailnet. Client riêng, `User-Agent` riêng.
- **Không** đặt DTO của truyện chung với DTO thiết bị.
- Xoá bỏ toàn bộ năng lực 5 phải là việc xoá một project, một thư mục frontend, và một mục
  navigation — không hơn.

### Phạm vi

Chỉ đọc. Danh sách truyện, tìm kiếm, chi tiết truyện, danh sách chương, trình đọc ảnh, và theo dõi
tiến độ đọc.

Không đăng nhập tài khoản MangaDex, không bình luận, không upload. Tiến độ đọc lưu trong SQLite của
hệ thống.

### Ràng buộc kỹ thuật

- **Chỉ dùng API công khai đã có tài liệu.** Không scrape HTML, không đọc ngược API nội bộ không
  công bố, không giả mạo request của trình duyệt để lách. Nếu một trang không có API công khai, ta
  không hỗ trợ trang đó.
- **Kiến trúc nhiều nguồn ngay từ đầu.** Định nghĩa một interface `IMangaSource` trong `Hub.Manga`,
  MangaDex là hiện thực đầu tiên. Không rải lời gọi MangaDex khắp nơi — phần còn lại chỉ biết tới
  `IMangaSource`.
- **Tôn trọng rate limit và `User-Agent`.** Tuân thủ giới hạn tần suất mà nhà cung cấp công bố, có
  backoff khi gặp HTTP 429. Không chạy song song ồ ạt để tải chương nhanh hơn.
- **Backend làm proxy ảnh, không để trình duyệt gọi thẳng MangaDex.** Xem mục dưới — đây là thay đổi
  quan trọng so với thiết kế cũ.
- **Trình đọc ảnh phải là phần khó nhất, hãy đối xử tương xứng.** Một chương là hàng chục ảnh lớn.
  Bắt buộc: tải theo yêu cầu (lazy), prefetch có giới hạn, và cache đĩa ở backend có mức trần dung
  lượng cấu hình được.

### Backend làm proxy — hệ quả của việc chuyển sang web

Đây là điểm **thay đổi so với thiết kế Android cũ**, và nó giải quyết luôn một vấn đề khó:

Trình duyệt **không thể** tự chọn giao thức HTTP/3, và cũng bị CORS chặn khi gọi thẳng
`api.mangadex.org`. Vì vậy **frontend không bao giờ gọi MangaDex trực tiếp.** Frontend gọi backend,
backend gọi MangaDex rồi trả kết quả về.

Điều này biến vấn đề HTTP/3 (từng là câu hỏi chặn đường lớn nhất của năng lực 5) thành **chuyện đơn
giản**: `HttpClient` của .NET hỗ trợ HTTP/3 sẵn, chỉ cần đặt `DefaultRequestVersion` và
`VersionPolicy`. Không cần Cronet, không cần thư viện lạ, không cần thêm dependency nào.

Kèm theo hai lợi ích nữa: cache ảnh nằm ở một chỗ (dùng chung cho mọi thiết bị), và điện thoại không
tốn 4G tải lại ảnh đã có trên PC.

### Đã xác minh — 2026-08-29

Toàn bộ mục "Chưa xác minh" trước đây **đã được kiểm chứng bằng thực nghiệm**. Kết quả đầy đủ nằm ở
**`docs/manga-api-research.md`** — đọc file đó trước khi viết dòng code đầu tiên của Năng lực 5. Tóm
tắt những gì ràng buộc kiến trúc:

**1. Có một rào chắn mạng, và nó đã được gỡ.** ISP tại VN chặn MangaDex **ở tầng TCP**. Nhưng
MangaDex quảng bá `alpn=h3` và **HTTP/3 qua QUIC (UDP 443) đi qua sạch** — 8/8 lần thử, trung bình
75 ms. Đã tải thật một ảnh trang 1.33 MB và một ảnh bìa 317 KB từ mạng gia đình, **không VPN, không
proxy**. Năng lực 5 khả thi; **không cần** sửa ranh giới cô lập ở mục trên.

> Đổi DNS / DoH **không** giải quyết được gì. Chặn nằm sau bước phân giải tên.

**2. Hai họ domain đi bằng hai giao thức khác nhau — đây là ràng buộc phải nhớ:**

| Họ domain | Vai trò | Giao thức dùng được |
|---|---|---|
| `*.mangadex.org` (`api`, `uploads`) | API + ảnh bìa | **Chỉ HTTP/3** — TCP bị RST |
| `*.mangadex.network` (node MD@H, report) | Ảnh trang + report | **Chỉ TCP (h2/h1.1)** — không có h3 |

Client ảnh **phải chọn giao thức theo host của `baseUrl` tại runtime**, không hardcode — vì
`/at-home/server/` có thể trả về host thuộc họ nào cũng được.

**3. Hệ quả lên stack — nay đã đơn giản đi.** Ràng buộc "cần client nói được HTTP/3" vẫn đúng, nhưng
với .NET thì `HttpClient` làm được sẵn. Câu hỏi chặn đường về Cronet của bản Android **đã biến mất**.

**4. Luồng ảnh và các con số đã xác minh** (chi tiết trong research doc):

- Rate limit, chính sách `User-Agent`: xác nhận từ tài liệu chính thức MangaDex.
- Luồng `/at-home/server/{chapterId}` là **hai bước**, đúng như dự đoán. `baseUrl` hết hạn sau ~15
  phút → HTTP **403**; trình đọc phải coi 403 là **tín hiệu refresh**, không phải lỗi.
- `baseUrl` là **chuỗi thuần** — không parse, không giả định định dạng, chỉ nối chuỗi.
- **Tuyệt đối không gửi header xác thực** tới image server.
- Endpoint report MD@H **vẫn còn hiệu lực và vẫn bắt buộc**. Đo thực tế cho thấy `/at-home/server/`
  trả về node bên thứ ba, nên nghĩa vụ report **sẽ kích hoạt thường xuyên**.
- **Bẫy đã gặp thật:** chapter có `pages = 0` vẫn khiến `/at-home/server/` trả **200 OK** với `hash`
  rỗng. Phải kiểm tra `pages > 0` và `externalUrl == null` **trước** khi gọi at-home.

**5. Rủi ro còn treo — không được quên:**

- ISP có thể chặn UDP 443 bất cứ lúc nào. UI **phải** coi "không tới được MangaDex" là trạng thái
  bình thường có xử lý tử tế, không phải crash.
- Số liệu trên đo bằng **Wi-Fi mạng cố định**. Vì backend nay chạy trên PC (luôn ở nhà, luôn dùng
  mạng cố định), **rủi ro "4G bóp UDP 443" đã biến mất** — điện thoại chỉ nói chuyện với backend qua
  tailnet, không gọi thẳng MangaDex nữa.

Quy tắc §9 vẫn áp dụng nguyên vẹn: tài liệu thắng trí nhớ. Đừng đoán lại các con số này — tra
`docs/manga-api-research.md`, và nếu nghi ngờ thì đo lại.

### Ranh giới pháp lý — nói thẳng

MangaDex cung cấp API công khai có tài liệu, nhưng phần lớn nội dung trên đó là bản dịch không có
giấy phép. Điều này chấp nhận được ở đây **chỉ vì** hệ thống là cá nhân, chạy trong tailnet riêng,
không phân phối, không quảng cáo, không thu tiền — đúng như §1 đã quy định.

Ràng buộc kèm theo: **không phân phối lại nội dung.** Không thêm tính năng chia sẻ chương ra ngoài,
không đưa truyện vào luồng sao lưu của năng lực 3, và **tuyệt đối không mở hệ thống ra Internet công
cộng** (§4 đã cấm điều này vì lý do bảo mật; ở đây có thêm một lý do nữa).

Nếu hệ thống này bao giờ rời khỏi phạm vi "chỉ mình tôi dùng", năng lực 5 phải bị gỡ trước tiên.

---

## 5a. Năng lực 6 — Điều khiển máy từ xa

**Giao cho MeshCentral (2026-09-01).** Hub không tự viết phần này nữa.

Trước đây §5a mô tả một hệ thống tự làm: agent .NET trên mỗi máy, sổ đăng ký thiết bị, bốn
endpoint điều khiển nguồn, nhật ký kiểm toán. Toàn bộ đã bị xoá — MeshCentral có sẵn agent
đóng gói cho Windows/Linux/macOS làm đủ những việc đó, cộng thêm Wake-on-LAN, điều khiển màn
hình và truyền file. Giữ cả hai là bảo trì hai thứ cùng làm một việc (§2.3).

Hub giữ vai **control plane và UI**: đăng nhập (§6), điều hướng, và nhúng MeshCentral ở
`/remote`. Xem `docs/meshcentral-setup.md`.

### Những ràng buộc vẫn còn giá trị

Chúng không biến mất cùng code — MeshCentral cũng chịu đúng các giới hạn vật lý này:

- **Tập hành động phải đóng.** Tắt, khởi động lại, ngủ, khoá màn hình. Không có "chạy lệnh tuỳ
  ý" — đó là quyết định gốc của §5a và vẫn giữ nguyên, dù nay do MeshCentral thực thi.
- **Không tự tắt máy đang chạy hub.** Tắt nó là cắt luôn đường vào hệ thống.
- **Mọi lệnh cần xác nhận trước khi gửi.** Hậu quả vật lý không lấy lại được.

### 5a.1. Đánh thức máy từ xa — đọc kỹ, đây là chỗ dễ hiểu sai nhất

**Mục tiêu:** bấm nút trên điện thoại ở bất cứ đâu và laptop ở nhà bật lên. Mục tiêu này **đạt
được**. Nhưng cách nó hoạt động không giống như tên gọi gợi ý, và hiểu sai chỗ này sẽ dẫn tới thiết
kế sai.

#### Sự thật phần cứng phải chấp nhận

**Một chiếc máy đã tắt không có kết nối Internet.** Khi Windows shutdown, hệ điều hành dừng, tầng
mạng biến mất, máy không còn địa chỉ IP. Thứ duy nhất còn sống là **card mạng**, ăn điện chờ, chạy
một mạch cố định chỉ làm đúng một việc: soi các khung Ethernet thô đi qua dây, tìm một mẫu bit cụ
thể (magic packet).

Card mạng đó **không giữ được kết nối TCP, không nói được TLS, không đăng nhập được vào đâu cả**. Nó
không ở trên Internet. Vì vậy:

> **Không tồn tại cách nào để đánh thức một máy đã tắt "chỉ cần nó có Internet".**
> Nếu ai đó (kể cả một agent AI) đề xuất giải pháp kiểu đó, giải pháp đó sai. Không có ngoại lệ.

Magic packet là **broadcast tầng 2**, mà broadcast thì router không chuyển đi. Đó là lý do WoL bị
giới hạn trong cùng một LAN.

#### Giải pháp: một thiết bị khác trong nhà làm "người đánh thức"

Ta không cần máy đã tắt có Internet. Ta cần **một thiết bị khác cùng LAN đang bật và có Internet**,
đóng vai người trung gian:

```
Điện thoại (ở bất cứ đâu)
    │  HTTPS qua tailnet
    ▼
Backend (chạy trên PC ở nhà)
    │  ra lệnh cho waker cùng LAN với máy đích
    ▼
Waker (PC / thiết bị luôn bật)
    │  magic packet, broadcast tầng 2, trong LAN
    ▼
Laptop đang tắt  →  bật lên
```

Với mô hình này, **mục tiêu ban đầu vẫn đạt được**: bạn ở ngoài đường vẫn đánh thức được laptop.
Điều kiện là trong nhà phải có **ít nhất một thiết bị luôn bật**.

#### Ai làm waker

| Waker | Ưu | Nhược |
|---|---|---|
| **PC chạy backend** | Không cần thêm gì; là mặc định | Không đánh thức được chính nó |
| **NAS** (khi có) | Chạy 24/7, ăn ít điện — **đích đến lý tưởng** | Chưa có |
| Router (nếu hỗ trợ) | Luôn bật | Phụ thuộc firmware, thường phải thao tác tay |

**Hệ quả quan trọng:** máy chạy backend **không tự đánh thức được chính nó**. Muốn đánh thức được
PC thì phải có một waker khác (NAS, hoặc laptop nếu nó đang bật). Đây là lý do §12 câu 9 (chọn máy
chạy backend) đáng trả lời sớm.

Khi có NAS, chuyển backend sang đó thì vấn đề này biến mất — NAS luôn bật và đánh thức được cả PC
lẫn laptop. Kiến trúc ở §3.3 đã chuẩn bị sẵn cho hướng này.

#### Những cách KHÔNG được dùng

Có vài cách để magic packet đi xuyên Internet. **Cấm dùng cả hai cách dưới đây**, vì chúng phá vỡ
§4 (không mở hệ thống ra Internet):

- **Port forwarding tới địa chỉ broadcast** — mở một cửa vào LAN nhà cho bất kỳ ai trên Internet.
  Bất kỳ ai gửi đúng gói cũng bật được máy của bạn.
- **Subnet-directed broadcast** — ngoài rủi ro trên, còn có thể bị lợi dụng để khuếch đại tấn công
  DDoS nhắm vào người khác.

Mật khẩu "SecureOn" của một số card mạng **không** phải giải pháp: nó truyền dạng thô, nghe lén được.

Đường đi đúng là **qua tailnet** — cách này đã mã hoá, đã xác thực, và không mở cổng nào ra ngoài.

#### Điều kiện kỹ thuật, phải kiểm chứng bằng tay trước khi code

Đây là việc **cấu hình và đo đạc**, không phải việc code. Làm trước, vì nếu phần cứng không hỗ trợ
thì viết code cũng vô ích:

1. **Bật WoL trong BIOS/UEFI** của máy đích (thường tên là "Wake on LAN", "Power on by PCI-E", hoặc
   "Resume by LAN").
2. **Bật trong driver card mạng** Windows: Device Manager → card mạng → Power Management → cho phép
   thiết bị đánh thức máy tính.
3. **Tắt Fast Startup của Windows.** Fast Startup khiến "shutdown" thực chất là một dạng hibernate
   lai, và trên nhiều máy nó làm WoL không hoạt động sau khi tắt máy. ⚠️ **Chưa xác minh được chi
   tiết** — tài liệu Microsoft không truy cập được lúc soạn file này. **Phải thử thật trên từng
   máy**, đừng tin lý thuyết.
4. **Ưu tiên mạng dây.** WoL qua Wi-Fi cần WoWLAN, mà phần lớn card Wi-Fi **không giữ liên kết ở
   trạng thái điện thấp** nên không nhận được magic packet. Nếu laptop chỉ dùng Wi-Fi, nhiều khả
   năng **không đánh thức được từ trạng thái tắt hẳn** — hãy đo, và nếu không được thì nói thẳng với
   người dùng thay vì hứa suông.
5. **Ghi lại kết quả đo cho từng máy** vào tài liệu vận hành: máy nào đánh thức được, từ trạng thái
   nào (sleep / hibernate / shutdown), qua dây hay Wi-Fi.

#### Ràng buộc thiết kế

- **Sleep và hibernate dễ đánh thức hơn shutdown.** Nếu đo thấy shutdown không đánh thức được, hãy
  khuyến khích người dùng dùng **sleep** thay vì shutdown — và nói rõ điều đó trên giao diện.
- **Wake không xác nhận được.** Gửi gói xong không biết máy có dậy không. Giao diện phải thể hiện
  đúng trạng thái: "đã gửi tín hiệu đánh thức", rồi **chờ agent của máy đó báo danh** (mốc hợp lý:
  ~90 giây) và báo thất bại tử tế nếu quá hạn. Không được hiển thị "đã bật" khi chưa có xác nhận.
- **Nút Wake chỉ hiện khi có waker khả dụng.** Nếu mọi thiết bị cùng LAN đều offline thì không thể
  đánh thức — giao diện phải nói rõ lý do, không để nút bấm được rồi thất bại im lặng.
- **Lưu địa chỉ MAC trong sổ đăng ký thiết bị** — đây là dữ liệu bắt buộc cho việc đánh thức, ghi
  lại khi agent còn online.

### Xác thực

Cần một phiên đăng nhập hợp lệ theo §6, cộng antiforgery token (§6.5 điều 5).

Không cần nhập lại mật khẩu mỗi lần — quy tắc đó trước đây là để bảo vệ chạy lệnh shell, mà nhóm
lệnh đó đã bị bỏ. Với tắt-mở máy thì xác nhận hai bước ở giao diện là đủ.

### Quan hệ với năng lực 4

Năng lực 4 (điều khiển màn hình) dùng client VNC có sẵn theo §2.3. Năng lực 6 là API riêng của ta.
Chúng không dùng chung code, và năng lực 6 **không** được dùng làm đường tắt để hiện thực năng lực 4.

---

## 5b. Năng lực 7 — Xem phim từ kho công cộng

### Phạm vi — hẹp, và hẹp có chủ đích

Duyệt, tìm kiếm, và xem phim từ **Internet Archive**. Chỉ thế.

Đây **không** phải trình xem phim vạn năng. Không Netflix, không phim đang chiếu rạp, không phim
thương mại. Nếu điều bạn muốn là xem phim mới, hãy dùng ứng dụng của nhà cung cấp — đừng nhét vào
hub này.

Số liệu đã đo: **28.482** phim trong bộ `feature_films`, trong đó **9.050** mục có khai báo giấy
phép. Chi tiết ở `docs/video-api-research.md` — **đọc file đó trước khi viết dòng code đầu tiên.**

### Vì sao chỉ Internet Archive, và vì sao cấm phần còn lại

Tìm "free movie API" sẽ ra `vidsrc`, `consumet`, `superembed`, `2embed` ở đầu bảng. **Cấm dùng tất
cả.** Lý do kỹ thuật, không phải lời răn:

| Vấn đề | Hệ quả với dự án này |
|---|---|
| Phục vụ phim thương mại không có giấy phép | Khác hẳn MangaDex (§5): nơi đó có API công khai, có tài liệu, có rate limit chính thức |
| Đổi tên miền liên tục vì bị gỡ | Dependency đổi địa chỉ vài tháng một lần = nợ kỹ thuật vĩnh viễn |
| Không hợp đồng API, không tài liệu | Đổi cấu trúc JSON bất cứ lúc nào, không cảnh báo |
| Kèm mã theo dõi và pop-up trong iframe | §6 cấm đưa thứ không kiểm soát được vào hệ thống có quyền tắt máy |

Và một điểm nặng hơn tất cả: hệ thống này **đã mở ra Internet qua Cloudflare Tunnel** (§4a). Nó
không còn là một app sideload chỉ mình dùng như giả định ban đầu của §1.

> **Không thêm nguồn phim nào khác vào hệ thống này.**
> Nếu về sau có nhu cầu "chỉ thêm một nguồn nhỏ thôi", câu trả lời mặc định là **không**.

### Giấy phép là bộ lọc, không phải lời hứa

Đây là ràng buộc quan trọng nhất của cả năng lực này.

**Không phải mọi thứ trong `feature_films` đều là public domain.** Đo thật: chỉ **37%** khai báo
giấy phép. Số còn lại **không khai báo gì** — nghĩa là *không xác minh được*, chứ không phải "mặc
định tự do".

Bắt buộc, không có ngoại lệ:

- **Mọi truy vấn phải lọc `licenseurl:[* TO *]`.** Đã kiểm chứng cú pháp này chạy đúng. Không có
  đường nào trong UI dẫn tới một mục không khai báo giấy phép.
- **Hiển thị giấy phép trên trang chi tiết**, kèm liên kết tới nguyên văn. Người xem phải biết mình
  đang xem gì.
- **Ghi nguồn Internet Archive** ở nơi dễ thấy.
- Một số mục là `by-nc-nd` (phi thương mại, không phái sinh) chứ không phải public domain thuần —
  **đọc và tôn trọng từng loại**, đừng gộp tất cả thành "miễn phí".

Bộ lọc này biến một câu hỏi pháp lý thành một điều kiện truy vấn. Đó chính là lý do năng lực này
khả thi trong khi "API phim free" nói chung thì không.

### Backend làm proxy — bắt buộc, và có một chi tiết dễ làm hỏng

Giống năng lực 5: **frontend không bao giờ gọi thẳng Internet Archive.** Frontend gọi backend,
backend gọi IA.

Nhưng video có một yêu cầu mà ảnh truyện không có: **tua**.

- Đã đo: IA trả **`Accept-Ranges: bytes`** và **206 Partial Content**. Đây là thứ cho phép nhảy tới
  giữa phim mà không tải hết 194 MB.
- ⚠️ **Proxy phải chuyển tiếp nguyên vẹn header `Range`** của trình duyệt, và trả lại đúng `206` +
  `Content-Range`. Nuốt mất `Range` là mất khả năng tua — người dùng phải chờ tải xong cả file mới
  xem được đoạn giữa. Đây là lỗi dễ mắc nhất khi viết proxy video.
- **Stream, đừng đệm cả file vào RAM.** Một phim là hàng trăm MB; đọc hết vào bộ nhớ rồi mới trả là
  cách chắc chắn nhất để giết backend. Dùng luồng, chép theo khối.

### URL tải là node vùng, không cố định

```
GET archive.org/download/{id}/{file}
  → 302 → dn710200.ca.archive.org/...
```

Giống hệt luồng MangaDex@Home ở §5 — và cùng một bẫy:

- **Phải theo redirect.** Không giả định host đích.
- **Không cache URL đã resolve** — node đổi được. Cache `identifier` + tên file, resolve lại mỗi
  phiên xem.
- **Không hardcode `dn*.archive.org`** ở bất cứ đâu, kể cả cấu hình.

### Tôn trọng hạ tầng của người khác

Internet Archive là tổ chức phi lợi nhuận, và ta đang dùng băng thông miễn phí của họ.

- **Không công bố con số rate limit** — nhưng trả **429**, có thể kèm `Retry-After`. Phải tôn trọng,
  không thử lại ngay.
- **Không tải song song nhiều luồng** để xem nhanh hơn.
- Đặt `User-Agent` định danh rõ ràng, giống quy tắc ở §5.
- **Không tải hàng loạt về máy.** Ta là trình xem, không phải công cụ nhân bản kho lưu trữ.

### Cô lập

Cùng khuôn với năng lực 5, vì cùng lý do:

- Backend trong `Hub.Video`, frontend trong `/src/features/video`.
- **`Hub.Video` không phụ thuộc bất cứ project nào khác ngoài `Hub.Core`.** Nó chỉ gọi ra Internet.
- **Không dùng chung `HttpClient` đã cấu hình** với phần tailnet.
- Xoá bỏ năng lực 7 phải là xoá một project, một thư mục frontend, một mục navigation.

### Metadata bổ sung — tuỳ chọn, không phải bắt buộc

Metadata của IA khá thô: nhiều mục thiếu poster, thiếu mô tả. TMDB có thể bù, nhưng:

- TMDB **chỉ có metadata**, không có phim. Nó không thay thế IA.
- Miễn phí cho phi thương mại, **bắt buộc ghi nguồn**. Dự án này hợp điều kiện.
- **Chưa đo được có vào được từ VN không** — xem `docs/video-api-research.md` §4. Đừng kết luận vội
  như đã từng làm sai với MangaDex.
- Là **thứ làm đẹp**. Năng lực 7 phải chạy đầy đủ khi không có TMDB.

### Thứ tự làm

Cuối hàng đợi, sau năng lực 5. Không bắt đầu khi Phase 0 chưa xong (§10).

Ba việc **đo đạc** phải làm trước khi viết code, ghi ở `docs/video-api-research.md` §6 — quan trọng
nhất là **đo tốc độ tải thật**. Lần đo đầu chỉ được ~51 KB/s trên đoạn 100 KB; nếu tốc độ thật duy
trì ở mức đó thì không phát trực tiếp được, và cả năng lực này phải thiết kế lại theo hướng tải
trước. Mẫu quá nhỏ để kết luận — nhưng phải đo trước khi cam kết.

---

## 6. Xác thực nội bộ và bảo mật — không thương lượng

Hệ thống này gom nội dung file, quyền truy cập từ xa vào PC, và quyền tắt-mở máy. Một phiên đăng
nhập bị chiếm là mất toàn bộ.

### 6.1. Tự làm xác thực — và những gì đi kèm

**Không dùng OAuth, không Google/Apple sign-in, không Auth0/Firebase/Clerk.** Đây là yêu cầu của
người dùng, và nó hợp lý cho một hệ thống chạy hoàn toàn nội bộ: phụ thuộc vào một dịch vụ ngoài để
đăng nhập vào máy tính của chính mình là vô lý, và nó chết khi mất mạng.

Nhưng phải nói thẳng cái giá: **tự làm xác thực là chỗ dễ làm sai nhất trong bảo mật web.** Vì vậy
mục này quy định chi tiết hơn các mục khác, và **không được tự ý đi chệch**.

Điều quan trọng nhất, nói trước: **không tự viết thuật toán mật mã.** Dùng thư viện có sẵn của
.NET/ASP.NET Core cho băm mật khẩu, sinh token, và quản lý cookie. "Tự làm xác thực" nghĩa là không
dùng dịch vụ ngoài — **không** phải là tự viết lại crypto.

### 6.2. Session hay JWT — đánh giá và quyết định

Câu hỏi này đã được cân nhắc nghiêm túc, không phải chọn theo thói quen. Ghi lại đầy đủ ở đây để
không phải tranh luận lại.

**JWT sinh ra để giải bài toán gì:** hệ thống nhiều service, không chia sẻ được session store, cần
xác thực stateless để mở rộng theo chiều ngang.

**Hệ thống này có bài toán đó không:** không. Một backend duy nhất, một người dùng, đã có sẵn SQLite.
Không có gì để JWT giải, nhưng vẫn phải trả đủ cái giá của nó.

Ba phương án, đánh giá thật:

| | A. Session cookie | B. JWT trong cookie `HttpOnly` | C. JWT trong `localStorage` |
|---|---|---|---|
| Chống XSS đọc token | ✅ JS không đọc được | ✅ JS không đọc được | ❌ **XSS là mất token** |
| Thu hồi tức thì (đăng xuất mọi thiết bị) | ✅ Xoá dòng trong DB | ⚠️ Cần thêm danh sách đen | ❌ Gần như không làm được |
| Độ phức tạp | Thấp | Trung bình (refresh token, quay vòng) | Trung bình |
| Hỗ trợ sẵn trong ASP.NET Core | ✅ Có sẵn | ✅ Có sẵn | ✅ Có sẵn |
| Hợp với kiến trúc một-backend | ✅ | ⚠️ Thừa | ⚠️ Thừa |

**Quyết định: dùng phương án A — session cookie.**

Lý do quyết định không phải "JWT không an toàn". **Phương án B an toàn ngang A** nếu làm đúng, và
hoàn toàn dùng được. Lý do là:

1. **Yêu cầu "đăng xuất tất cả thiết bị khi mất máy" là yêu cầu bắt buộc của hệ thống này** (§6.3).
   JWT đúng nghĩa thì không thu hồi được — muốn thu hồi phải tra DB mỗi request, mà lúc đó nó *chính
   là* session, chỉ thêm một lớp phức tạp vô ích.
2. Không có lợi ích nào của JWT được dùng đến. Trả giá mà không nhận lại gì.
3. Ít code hơn ⇒ ít chỗ sai hơn. Với một hệ thống có quyền tắt máy từ xa, đây không phải chuyện nhỏ.

**Phương án C bị cấm.** Không phải vì JWT, mà vì `localStorage`: JavaScript đọc được, nên một lỗ
hổng XSS ở bất kỳ đâu trong frontend là mất token. Không dùng `localStorage` hay `sessionStorage`
để lưu token, dù ở dạng nào.

**Khi nào thì xét lại và chuyển sang B:** nếu sau này hệ thống tách thành nhiều service cần xác
thực chung, hoặc có một client không dùng được cookie. Cả hai đều chưa xảy ra và có thể không bao
giờ xảy ra. Nếu xảy ra: JWT ngắn hạn (~15 phút) trong cookie `HttpOnly`, cộng refresh token quay
vòng lưu trong DB — **không** phải JWT dài hạn trong `localStorage`.

### 6.3. Mô hình xác thực

Một người dùng duy nhất. Không có bảng `Users` nhiều dòng, không phân quyền, không vai trò.

**Mật khẩu:**

- Băm bằng **ASP.NET Core Identity `PasswordHasher<T>`** (PBKDF2 với tham số mặc định hiện hành).
  **Không** MD5, **không** SHA-256 trần, **không** tự trộn salt.
- Mật khẩu **không bao giờ** nằm trong source, `appsettings.json`, hay biến môi trường ở dạng thô.
  Chỉ lưu hash trong SQLite.
- Lần chạy đầu: nếu chưa có mật khẩu, hệ thống bắt đặt mật khẩu qua giao diện, và **chỉ chấp nhận
  yêu cầu này từ `localhost`** (máy chạy backend). Không cho đặt mật khẩu lần đầu từ xa.
- Đổi mật khẩu phải nhập mật khẩu cũ, và **huỷ toàn bộ phiên khác** sau khi đổi.

**Phiên đăng nhập:**

- **ASP.NET Core Authentication cookie** có sẵn. Không tự sinh, không tự quản token.
- Cookie bắt buộc: **`HttpOnly`**, **`Secure`**, **`SameSite=Strict`**.
- **Phiên lưu trong SQLite**, không chỉ nằm trong cookie đã ký. Đây là điều kiện để thu hồi được.
  Mỗi dòng: id phiên, thiết bị, thời điểm tạo, lần dùng cuối, IP tailnet.
- Thời hạn: mặc định **30 ngày**, gia hạn trượt khi còn hoạt động.
- Bắt buộc có: màn hình **liệt kê phiên đang mở** và nút **đăng xuất tất cả thiết bị**. Đây không
  phải tính năng "nice to have" — nó là phương án ứng phó khi mất điện thoại.
- **Xoay session id sau khi đăng nhập thành công** (chống session fixation).

**Chống dò mật khẩu:**

- Giới hạn số lần đăng nhập sai: khoá tăng dần theo thời gian sau vài lần thất bại.
- Thời gian phản hồi phải như nhau dù sai tài khoản hay sai mật khẩu.
- Ghi nhật ký mọi lần đăng nhập thất bại (thời điểm, IP tailnet) — nhưng **không log mật khẩu đã
  nhập**, kể cả khi sai.

### 6.4. Tailnet là lớp phòng thủ thứ nhất, không phải duy nhất

Hệ thống chỉ vào được từ tailnet, nên kẻ tấn công phải có thiết bị đã nằm trong tailnet. Điều đó
mạnh — nhưng **không được coi là đủ**.

Lý do: một chiếc điện thoại đã cài Tailscale và đang mở phiên đăng nhập, nếu bị mất, là **đã vượt cả
hai lớp**. Vì vậy vẫn cần mật khẩu, vẫn cần đăng xuất từ xa.

**Không bao giờ** viết code kiểu "request đến từ 100.x.y.z nên bỏ qua xác thực".

### 6.5. Quy tắc bảo mật chung

1. **Không secret trong source, ever.** Không API key, không tailnet auth key, không thông tin đăng
   nhập cloud trong repo. Dùng **.NET User Secrets** khi phát triển, biến môi trường khi chạy thật.
2. **Thông tin xác thực của rclone và các dịch vụ khác** lưu mã hoá trong SQLite, khoá nằm ngoài DB.
   Không plaintext.
3. **Đích sao lưu lưu blob đã mã hoá phía client.** Dùng remote `crypt` của rclone. Nhà cung cấp
   cloud không bao giờ được thấy plaintext.
4. **Không log thứ nhạy cảm.** Không mật khẩu, không token, không cookie, không đường dẫn file —
   kể cả ở môi trường phát triển. Ngoại lệ duy nhất: nhật ký kiểm toán của §5a.
5. **Chống CSRF.** `SameSite=Strict` chặn phần lớn, nhưng các endpoint thay đổi trạng thái vẫn phải
   có antiforgery token của ASP.NET Core. Năng lực 6 đặc biệt cần điều này.
6. **Validate mọi input ở backend.** Frontend validate chỉ để trải nghiệm tốt hơn; nó không phải lớp
   bảo mật vì người dùng sửa được. Đặc biệt: mọi đường dẫn file của năng lực 2 phải chống
   **path traversal** (`../`) — kiểm tra đường dẫn đã chuẩn hoá có nằm trong thư mục cho phép không.
7. **Không hiện chi tiết lỗi ra frontend.** Stack trace vào log, người dùng chỉ thấy thông báo chung.

---

## 7. Quy ước viết code

### Backend (C#)

- Bật **nullable reference types**. Bật `TreatWarningsAsErrors` cho các cảnh báo nghiêm trọng.
- **`async`/`await` xuyên suốt.** Không `.Result`, không `.Wait()` — deadlock đang chờ sẵn.
- Kết quả thao tác có thể thất bại: trả về kiểu `Result` tường minh, không ném exception cho luồng
  nghiệp vụ bình thường.
- **Dependency injection** cho mọi thứ. Không `static` giữ trạng thái.
- Đặt tên theo quy ước .NET: `PascalCase` cho public, `_camelCase` cho field private.
- Mỗi endpoint làm một việc. Không endpoint nhận `action` rồi rẽ nhánh bên trong.

### Frontend (TypeScript/React)

- **Không `any`.** Nếu chưa biết kiểu, dùng `unknown` rồi thu hẹp.
- Component là hàm, dùng hooks. Không class component.
- **Mọi lời gọi API đi qua TanStack Query.** Không `fetch` rải rác trong component.
- Kiểu dữ liệu API **sinh từ OpenAPI**, không viết tay (§3).
- Tách component hiển thị và component có logic. Logic phức tạp đưa vào custom hook.
- Xử lý đủ ba trạng thái: **loading, error, empty**. Bỏ sót trạng thái error là lỗi, không phải thiếu
  sót nhỏ.

### Chung

- Mỗi phần logic nghiệp vụ mới đều phải có test. Backend: **xUnit**. Frontend: **Vitest**.
- Comment giải thích *tại sao*, không giải thích *cái gì*. Code tự nói được nó làm gì.
- Commit messages: Conventional Commits (`feat:`, `fix:`, `refactor:`, `chore:`).
- Định dạng tự động: `dotnet format` cho C#, **Prettier + ESLint** cho frontend. Chạy trong CI, phải
  pass.

---

## 8. Build và kiểm chứng

```bash
# Backend
dotnet build                      # build solution
dotnet test                       # chạy test
dotnet run --project backend/Hub.Api

# Frontend
npm run dev                       # dev server, có hot reload
npm run build                     # build ra file tĩnh
npm run test                      # Vitest
npm run lint                      # ESLint
```

**Định nghĩa "hoàn thành" cho mọi thay đổi:**

1. `dotnet build` và `npm run build` thành công, không có warning mới.
2. `dotnet test` và `npm run test` pass, gồm cả test mới cho logic mới.
3. `npm run lint` và `dotnet format --verify-no-changes` sạch.
4. Thay đổi giao diện đã **mở thử trên điện thoại thật** (cả iPhone và Android nếu có), không chỉ
   thu nhỏ cửa sổ trình duyệt trên desktop. Đây là hệ thống mobile-first — kiểm chứng trên máy thật.
5. Nếu thay đổi động tới xác thực, mạng, hoặc năng lực 6: kiểm chứng thủ công **từ một thiết bị
   khác qua tailnet**, không chỉ trên `localhost`. Hành vi cookie và HTTPS khác nhau đáng kể.

Không bao giờ đánh dấu một task là xong chỉ vì "nó build được".

---

## 9. Cách làm việc trên dự án này

- **Hỏi trước khi scaffold.** Trước khi tạo hơn ba file mới, hãy trình bày kế hoạch và chờ.
- **Mỗi lần một năng lực.** Không bắt đầu năng lực *n+1* khi *n* chưa xong. Không thêm các refactor
  kiểu "tiện tay làm luôn".
- **Không có phần triển khai giả.** Không hàm ném `NotImplementedException`, không dữ liệu giả trả
  về từ API, không màn hình mock được trình bày như đã chạy được. Nếu chưa xây được thứ gì đó, hãy
  nói ra và dừng lại.
- **Không thêm dependency mới mà không hỏi.** Nêu rõ nó làm gì, kích thước, tình trạng bảo trì, và
  nó thay thế cho cái gì. Áp dụng cho cả NuGet lẫn npm — hệ sinh thái npm đặc biệt dễ phình.
- **Khi tài liệu và trí nhớ của bạn mâu thuẫn, tài liệu thắng.** Tra cứu, đừng đoán.
- **Nói cho người dùng biết khi họ sai.** Nếu một hướng tiếp cận được yêu cầu là ý tồi, hãy nói thẳng
  và giải thích tại sao trước khi triển khai. Không xây thứ mà bạn tin là hỏng.
- Ưu tiên xoá code hơn thêm cờ (flag). Ưu tiên giải pháp nhàm chán.

---

## 10. Giai đoạn hiện tại

**Phase 0 — Nền móng. Chưa xây gì cả.**

Tiêu chí hoàn tất Phase 0:

- [ ] Tailscale đã cài và kiểm chứng trên **cả bốn thiết bị** (PC, laptop, Android, iPhone); tất cả
      thấy nhau
- [ ] MagicDNS bật; truy cập được backend bằng tên miền, không phải IP
- [ ] `tailscale cert` cấp chứng chỉ; HTTPS chạy, **không có cảnh báo trình duyệt trên iPhone**
- [ ] Solution .NET tạo xong theo bố cục §3; `dotnet build` xanh
- [ ] Backend bind vào địa chỉ tailnet, **thất bại rõ ràng** nếu không tìm thấy (§4)
- [ ] Frontend React + Vite tạo xong; `npm run build` xanh; backend phục vụ được file tĩnh
- [ ] Tailwind + shadcn/ui cài xong; render thử một component để xác nhận đường dẫn và theme chạy
- [ ] Endpoint `/health` chạy; sinh kiểu TypeScript từ OpenAPI hoạt động
- [ ] **Xác thực chạy được đầu-cuối**: đặt mật khẩu lần đầu từ localhost, đăng nhập từ điện thoại,
      cookie giữ phiên, đăng xuất tất cả thiết bị hoạt động
- [ ] Mở được giao diện trên iPhone và Android qua tailnet, thấy trạng thái `/health`

Không bắt đầu năng lực nào cho tới khi mọi ô ở trên đã được tick. **Đặc biệt: không bắt đầu năng lực
6 cho tới khi xác thực đã hoàn chỉnh và được kiểm chứng.**

---
## 11. Nhật ký quyết định

Ghi thêm vào đây mỗi khi một quyết định kiến trúc được đưa ra hoặc bị đảo ngược. Ngày, quyết định,
lý do.

| Ngày | Quyết định | Lý do |
|---|---|---|
| 2026-08-18 | Tailscale làm lớp transport | Loại NAT traversal, định danh, và mã hoá ra khỏi phạm vi |
| 2026-08-18 | Giao cho rclone / Syncthing / VNC thay vì tự viết lại | Kiểm soát phạm vi; đây là các bài toán đã được giải |
| 2026-08-29 | Thêm năng lực 5: đọc truyện qua API công khai | Người dùng muốn có; cô lập triệt để để có thể gỡ bỏ dễ dàng |
| 2026-08-29 | Xác minh API MangaDex bằng thực nghiệm, ghi vào `docs/manga-api-research.md` | Kết quả vẫn còn giá trị nguyên vẹn sau khi đổi stack |
| 2026-08-29 | ISP chặn MangaDex ở tầng **TCP**; **HTTP/3 (QUIC) đi qua được** | Kiểm chứng bằng đo đạc thật, không VPN |
| 2026-08-29 | Thêm năng lực 6: chạy lệnh / điều khiển nguồn từ xa | Người dùng muốn điều khiển giữa hai desktop |
| 2026-08-29 | Năng lực 6 tách hai nhóm; nhóm shell **mặc định tắt** | Không được để phiên đăng nhập bị chiếm tự bật quyền chạy lệnh |

### Đảo ngược lớn — 2026-08-29

| Ngày | Quyết định | Lý do |
|---|---|---|
| 2026-08-29 | **Bỏ hoàn toàn ứng dụng Android.** Hệ thống thành web: backend .NET + frontend React | Tương thích đa thiết bị. Người dùng có cả iPhone; viết app iOS là nhân đôi công sức, cần máy Mac và tài khoản Apple Developer trả phí. Web chạy trên mọi thứ có trình duyệt |
| 2026-08-29 | Đảo ngược 2026-08-18: "desktop agent bằng Kotlin/JVM, một ngôn ngữ cho toàn dự án" | Ngôn ngữ chung nay là **C#**, không phải Kotlin. Nguyên tắc "một ngôn ngữ" vẫn giữ, chỉ đổi ngôn ngữ. Kotlin, Compose, Room, Ktor, KMP `:shared` — **loại bỏ toàn bộ** |
| 2026-08-29 | Đảo ngược quyết định C# làm "client thứ hai" (ghi cùng ngày, trước đó) | C# nay là **backend chính**, không còn là client UI bổ sung. Mục §3a cũ bị xoá |
| 2026-08-29 | **Bỏ hẳn năng lực theo dõi vị trí** | Trình duyệt không theo dõi vị trí nền được — đóng tab là dừng. Không có cách vòng tử tế. Năng lực 1 thu hẹp còn presence (online/offline) |
| 2026-08-29 | Xoá toàn bộ §4 cũ (ràng buộc Android: foreground service, `MANAGE_EXTERNAL_STORAGE`, `FusedLocationProviderClient`) | Không còn liên quan. §4 nay nói về miền nội bộ và HTTPS |
| 2026-08-29 | Năng lực 2 thu hẹp: **chỉ duyệt file trên PC/laptop**, không duyệt file trong điện thoại | Trình duyệt chỉ thấy file người dùng tự chọn. Đây là cái giá của web, ghi rõ ở §2.1 |
| 2026-08-29 | **Xác thực tự làm, cấm OAuth và mọi dịch vụ đăng nhập ngoài** | Yêu cầu của người dùng; hợp lý cho hệ thống nội bộ — không phụ thuộc dịch vụ ngoài để vào máy của chính mình, và vẫn chạy khi mất mạng |
| 2026-08-29 | Cookie session `HttpOnly`, **không** JWT trong `localStorage` | `localStorage` bị XSS đọc được. Đây là lỗi kinh điển của người tự làm xác thực |
| 2026-08-29 | **Đánh giá lại JWT theo yêu cầu; giữ session cookie (phương án A)** | JWT *dùng được* và JWT-trong-cookie-`HttpOnly` an toàn ngang session. Nhưng JWT giải bài toán stateless nhiều-service mà hệ thống này không có, trong khi yêu cầu "đăng xuất mọi thiết bị" lại đòi tra DB mỗi request — lúc đó JWT *chính là* session, chỉ phức tạp hơn. Ba phương án và tiêu chí xét lại ghi ở §6.2 |
| 2026-08-29 | **Phiên lưu trong SQLite**, không chỉ nằm trong cookie đã ký | Điều kiện bắt buộc để thu hồi phiên tức thì khi mất thiết bị |
| 2026-08-29 | **Bỏ hẳn nhóm B của năng lực 6 (chạy lệnh shell)** | Người dùng chốt chỉ cần tắt-mở máy. Xoá bỏ rủi ro lớn nhất của toàn dự án: phiên bị chiếm không còn dẫn tới thực thi mã tuỳ ý |
| 2026-08-29 | Năng lực 6 dùng chung **sổ đăng ký thiết bị** với năng lực 1 | Cùng một dữ liệu (thiết bị, trạng thái, MAC cho WoL). Hai sổ riêng là nguồn sai lệch |
| 2026-08-29 | **shadcn/ui + Tailwind** cho frontend | Mã chép vào repo nên sở hữu được và không phình dependency; hợp hệ thống mobile-first cá nhân |
| 2026-08-29 | Đánh thức từ xa dùng mô hình **waker trung gian**, không phải "wake qua Internet" | Máy đã tắt **không có** kết nối Internet — card mạng chỉ soi khung Ethernet thô, không giữ nổi TCP/TLS. Mục tiêu "bấm từ điện thoại ở bất cứ đâu" vẫn đạt được, nhờ một thiết bị cùng LAN đang bật làm trung gian (§5a.1) |
| 2026-08-29 | **Cấm** port forwarding và subnet-directed broadcast để đánh thức qua Internet | Cả hai mở LAN nhà ra Internet, phá vỡ §4; SDB còn có thể bị lợi dụng khuếch đại DDoS. Đường đúng là qua tailnet |
| 2026-08-29 | Sổ đăng ký thiết bị thêm **nhãn LAN**, **khả năng làm waker**, **kết quả đo đánh thức** | Hệ thống phải biết máy nào đánh thức được máy nào; và khả năng đánh thức phụ thuộc phần cứng nên phải **đo**, không suy luận |
| 2026-08-29 | **.NET 10 LTS** thay cho .NET 8 ghi ban đầu ở §3 | Máy phát triển đã có sẵn SDK 10.0.300; .NET 10 là LTS mới hơn, hỗ trợ dài hơn. Không phải cài thêm gì. Ghim bằng `global.json` |
| 2026-08-29 | Thêm `PROGRESS.md` theo dõi tiến trình, tách khỏi `CONTEXT.md` | `CONTEXT.md` là **luật** (ổn định, ít đổi); `PROGRESS.md` là **trạng thái** (đổi liên tục). Trộn chung khiến file luật bị nhiễu |
| 2026-08-29 | Truy cập qua **MagicDNS + `tailscale cert`**, không tự dựng DNS/CA | Chứng chỉ Let's Encrypt hợp lệ, không cảnh báo trình duyệt, iPhone không phải cài profile thủ công |
| 2026-08-29 | Backend làm **proxy ảnh** cho năng lực 5 | Trình duyệt không chọn được HTTP/3 và bị CORS chặn. Đồng thời **xoá bỏ câu hỏi chặn đường về Cronet** — `HttpClient` của .NET nói HTTP/3 sẵn |
| 2026-08-29 | Backend thiết kế để chuyển sang NAS: lõi không phụ thuộc Windows | Đích đến là NAS chạy 24/7. Mã riêng cho Windows nằm ở agent, sau interface |
| 2026-08-31 | Mở ra Internet qua Cloudflare Tunnel, bỏ ràng buộc "chỉ tailnet" của §1 | Yêu cầu của người dùng: vào được từ mọi thiết bị không cần cài Tailscale. Đổi lại phải tự làm rate limit và header bảo mật — thứ tailnet vốn che. Vẫn không port forwarding. Xem §4a |
| 2026-08-31 | Năng lực 6 dùng MeshCentral thay vì agent tự viết | §2.3 — tái sử dụng, đừng phát minh lại. Được sẵn Wake-on-LAN, agent đa nền tảng, giao diện mobile |
| 2026-08-29 | Trong container: bind `0.0.0.0`, chặn bằng cổng publish gắn IP tailnet | Container có netns riêng nên `0.0.0.0` không phơi ra mạng nhà. Cách chuẩn của Docker, chạy được trên mọi NAS. Đã kiểm chứng: từ LAN không vào được, qua tailnet trả 200 |
| 2026-08-31 | Thêm năng lực 7: xem phim, **chỉ từ Internet Archive** | Nguồn duy nhất vừa hợp pháp, vừa cho stream thật, vừa không cần khoá. Đo thật: 28.482 phim, search/metadata trả 200, tải file trả 206 |
| 2026-08-31 | **Cấm** mọi API phim kiểu `vidsrc`/`consumet`/`superembed` | Phục vụ phim thương mại không giấy phép; đổi tên miền liên tục; không hợp đồng API; kèm mã theo dõi. Nặng hơn cả: hệ thống nay đã mở ra Internet (§4a), không còn là app sideload riêng tư như giả định gốc của §1 |
| 2026-08-31 | Mọi truy vấn phim **bắt buộc lọc `licenseurl:[* TO *]`** | Đo được chỉ **37%** mục trong `feature_films` khai báo giấy phép (9.050/28.482). Không khai báo ≠ tự do. Bộ lọc biến câu hỏi pháp lý thành điều kiện truy vấn |
| 2026-08-31 | Proxy video **phải chuyển tiếp header `Range`** | IA trả `Accept-Ranges: bytes` và 206 — đây là thứ cho phép tua. Nuốt mất `Range` là mất tua, và đây là lỗi dễ mắc nhất khi viết proxy video |
| 2026-08-31 | **Chưa** kết luận TMDB có bị chặn ở VN hay không | `api.themoviedb.org` không kết nối được, nhưng `curl` trên máy này **không có HTTP/3** — đúng công cụ thiếu năng lực đã từng làm kết luận sai về MangaDex. Phải đo lại bằng `HttpClient` của .NET |
| 2026-09-01 | Địa chỉ MeshCentral chọn theo **Host của request**, không phải một URL cứng | Tên MagicDNS chỉ phân giải được trong tailnet; trả nó cho người vào qua Internet thì trình duyệt báo không tìm thấy máy chủ. Dùng Host chứ không dùng IP client vì sau Cloudflare Tunnel mọi request đều đến từ loopback |
| 2026-09-01 | **Năng lực 2 giao cho MeshCentral**, xoá trang `/files` | MeshCentral đã có duyệt và truyền file trong chính agent của nó. Dựng thêm trang duyệt file của ta là viết lại thứ đã có — đúng điều §2.3 cấm. `/files` chuyển hướng sang `/remote` cho ai đã lưu đường dẫn cũ |
| 2026-09-01 | **Xoá hẳn agent tự viết**: Hub.Agent, Hub.Windows, 4 endpoint điều khiển nguồn, sổ đăng ký thiết bị, 2 bảng DB | MeshCentral đã làm đủ những việc đó. Giữ cả hai là bảo trì hai thứ cùng làm một việc (§2.3). Hệ quả tốt: `Hub.Core` không còn phụ thuộc Windows (hợp §3.3), và hub không còn endpoint nào đổi trạng thái vật lý của máy |

---

## 12. Câu hỏi mở — không đoán, hãy hỏi

Các câu đã chốt (thư viện UI, chạy lệnh shell) đã chuyển vào §11. Còn lại:

1. **Cách cập nhật trạng thái hiện diện theo thời gian thực?** — *nên chốt trong Phase 1.*
   Polling định kỳ (đơn giản nhất, tốn pin điện thoại), **SSE** (một chiều, nhẹ, đủ cho presence),
   hay **SignalR** (hai chiều, mạnh hơn, nặng hơn)?
   Nay nhóm shell đã bị bỏ, nhu cầu hai chiều giảm hẳn — **SSE có lẽ là đủ**. Năng lực 4 dùng noVNC
   với kênh riêng của nó, không phụ thuộc lựa chọn này.
2. **Điều khiển màn hình trong trình duyệt bằng gì?** **noVNC** là lựa chọn rõ ràng nhất (chạy trong
   trình duyệt, cần server VNC trên máy Windows). RDP trong trình duyệt khó hơn nhiều. Kiểm tra
   trước xem mỗi máy chạy bản Windows nào.
3. **Có làm PWA không?** Cài về màn hình chính cho giống app native, chạy toàn màn hình. Rẻ, nhưng
   thêm service worker và cache — có thể gây khó hiểu khi debug. Đáng làm sau khi mọi thứ chạy ổn.
4. Dùng nhà cung cấp cloud nào để sao lưu? Ảnh hưởng tới cấu hình remote của rclone, không ảnh hưởng
   kiến trúc.
5. Model và giao thức NAS (SMB / NFS / SFTP)? Hoãn cho tới khi có NAS. Lưu ý: khi có NAS thì backend
   sẽ chuyển sang chạy trên đó (§3.3).
6. Ngoài MangaDex, có định thêm nguồn truyện nào nữa không? Ảnh hưởng tới việc `IMangaSource` cần
   trừu tượng tới mức nào. Nếu chỉ mãi một nguồn thì đừng thiết kế quá mức.
7. Có cần tải chương về đọc offline không? Với kiến trúc web, "offline" nghĩa là cache ở backend —
   khác hẳn nghĩa cũ trên Android. Làm rõ trước khi thiết kế.
8. Sao lưu chính file SQLite của hệ thống thế nào? Nó chứa hash mật khẩu, phiên đăng nhập, và thông
   tin xác thực đã mã hoá. Mất nó là mất cấu hình; lộ nó là vấn đề bảo mật.
9. **Máy nào chạy backend — PC hay laptop?** — *đáng trả lời sớm, ảnh hưởng tới năng lực 6.*
   Máy đó phải bật thì hệ thống mới hoạt động, **không tắt được chính nó qua giao diện** (§5a điều
   5), và **không tự đánh thức được chính nó** (§5a.1). Chọn máy nào thì máy đó là máy duy nhất
   không đánh thức được từ xa — cho tới khi có NAS.
10. **Đo khả năng đánh thức của từng máy** (§5a.1 mục điều kiện kỹ thuật). Đây là việc **đo bằng
    tay, không phải code**, và phải làm **trước** khi viết năng lực 6 — nếu phần cứng không hỗ trợ
    thì code cũng vô ích. Cần biết: mỗi máy đánh thức được từ trạng thái nào (sleep / hibernate /
    shutdown), qua dây hay Wi-Fi, và Fast Startup có làm hỏng không.
11. **Tốc độ tải từ Internet Archive có đủ để phát trực tiếp không?** — *chặn đường của năng lực 7.*
    Lần đo đầu chỉ được **~51 KB/s** trên đoạn 100 KB — quá chậm nếu duy trì, nhưng mẫu quá nhỏ để
    kết luận. Phải đo lại với đoạn lớn hơn, nhiều thời điểm trong ngày. Nếu tốc độ thật thấp thì cả
    năng lực 7 phải thiết kế lại theo hướng **tải trước rồi mới xem**, không phát trực tiếp.
12. **Bao nhiêu phim trong tập dữ liệu là định dạng trình duyệt phát được?** MP4/h.264 thì phát
    thẳng; MPEG2/MKV thì phải chuyển mã — mà chuyển mã là việc nặng, và §2.3 cấm tự viết codec. Nếu
    tỷ lệ MP4 thấp, phải lọc thêm theo định dạng và chấp nhận kho phim nhỏ hơn.
13. Có dùng TMDB để bù metadata (poster, mô tả) cho năng lực 7 không? Trả lời được sau khi đo lại
    khả năng truy cập (§11 nhật ký 2026-08-31). Là thứ **làm đẹp** — năng lực 7 phải chạy đủ khi
    không có nó.




