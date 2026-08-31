# Cài MeshCentral cho năng lực 6

Điều khiển thiết bị từ xa dùng **[MeshCentral](https://github.com/Ylianst/MeshCentral)** (Apache 2.0)
thay vì tự viết agent.

## Vì sao dùng lại thay vì tự làm

CONTEXT.md §2.3: *"Hệ thống của ta là một control plane và một UI. Phần việc nặng giao cho công cụ
đã được kiểm chứng."*

MeshCentral cho sẵn những thứ tự làm sẽ tốn rất nhiều công mà không tốt hơn:

| Thứ | Tự làm | MeshCentral |
|---|---|---|
| Agent | Phải viết và đóng gói cho từng hệ điều hành | Installer sẵn, đã ký số, Windows/Linux/macOS |
| **Wake-on-LAN** | Chưa làm được — §12 câu 10 chặn | Có sẵn, kèm Intel AMT |
| Điều khiển màn hình | Năng lực 4, chưa bắt đầu | Có sẵn, chạy trong trình duyệt |
| Giao diện điện thoại | Phải tự dựng | Có view mobile riêng (8.372 dòng) |

Hub **vẫn là control plane và UI**: nó giữ đăng nhập (§6), bố cục, điều hướng, và các năng lực
khác. Tab "Điều khiển máy" nhúng MeshCentral vào bằng iframe.

## Cài đặt

Cần Node.js. Không cần MongoDB — mặc định dùng NeDB nhúng, đủ cho hệ thống một người dùng.

```bash
mkdir D:\App\MeshCentral
cd D:\App\MeshCentral
npm install meshcentral
```

Đặt trên **ổ D** — ổ C sắp đầy. Chiếm khoảng 504 MB.

## Cấu hình

Tạo `meshcentral-data/config.json` — bản đang chạy trên máy này:

```json
{
  "settings": {
    "cert": "hub.tailnet-example.ts.net",
    "port": 4430,
    "redirPort": 8008,

    "portbind": "100.100.100.100",
    "redirportbind": "100.100.100.100",
    "agentportbind": "100.100.100.100",

    "webrtcconfig": { "iceServers": [] },
    "mpsport": 0,
    "amtmanager": false,
    "allowFraming": true,
    "allowedFramingOrigins": [
      "https://hub.tailnet-example.ts.net:7189",
      "https://localhost:5173",
      "https://localhost:7189"
    ],
    "newAccounts": false
  },
  "domains": {
    "": { "title": "Hub", "title2": "Thiết bị", "minify": true, "newAccounts": false }
  }
}
```

**`portbind` là khoá đúng để bind theo §4** — không phải `bindInterface`. Tên sai bị **bỏ qua im
lặng**: MeshCentral vẫn chạy nhưng nghe trên mọi địa chỉ. Đã gặp thật lúc cấu hình, và kiểm chứng
được bằng cách gọi từ IP LAN.

Cần cả ba khoá: `portbind` (web), `redirportbind` (chuyển hướng HTTP), `agentportbind` (agent).

**Cách kiểm chứng §4** — làm lại mỗi khi đụng vào phần mạng:

```bash
curl -k https://100.100.100.100:4430/    # phải 200
curl -k https://192.168.1.10:4430/     # phải KHÔNG kết nối được
```

**`allowFraming` + `allowedFramingOrigins` là bắt buộc.** Thiếu chúng thì CSP `frame-ancestors` chặn
iframe và tab Remote hiện trắng. Đã kiểm chứng: cấu hình đúng thì header trả về

```
frame-ancestors 'self' https://hub.tailnet-example.ts.net:7189 https://localhost:5173
```

`localhost:5173` là dev server của Vite — bỏ đi khi chạy thật.

## Chứng chỉ — không dùng được bản của Tailscale

`tailscale cert` cấp chứng chỉ **ECDSA** (`id-ecPublicKey / prime256v1`), nhưng MeshCentral đọc
chứng chỉ qua thư viện `node-forge` và nó **chỉ hỗ trợ RSA**. Chép chứng chỉ Tailscale vào sẽ chết
lúc khởi động:

```
Error: Cannot read public key. OID is not RSA.
```

Nên để MeshCentral **tự sinh chứng chỉ**. Hệ quả: trình duyệt cảnh báo lần đầu, phải bấm chấp nhận.
Chấp nhận được vì tailnet đã mã hoá đầu-cuối (§2.2) — nhưng khác với hub, nơi chứng chỉ Tailscale
dùng bình thường.

## Chạy

```bash
node node_modules/meshcentral
```

Lần đầu nó tự sinh chứng chỉ và ký các file agent (mất khoảng 30 giây). Tài khoản **đầu tiên** đăng
ký sẽ là quản trị viên — tạo ngay, đừng để trống.

Chạy thật thì cài làm Windows Service để khởi động cùng máy:

```bash
node node_modules/meshcentral --install
```

## Khai địa chỉ cho hub

```bash
dotnet user-secrets --project backend/Hub.Api set "MeshCentral:Url" "https://hub.tailnet-example.ts.net:4430"
```

Hoặc biến môi trường khi chạy thật: `MeshCentral__Url`.

⚠️ Phải là địa chỉ **trình duyệt của người dùng** gọi tới được — iframe chạy trên máy họ, không phải
trên máy chạy hub. Dùng `localhost` thì điện thoại không mở được.

Khởi động lại backend sau khi khai.

## KHÔNG dùng `LANonly`

Nhìn tên thì `LANonly: true` có vẻ đúng tinh thần §4 — nhưng nó phá agent.

Ở chế độ đó MeshCentral ghi **`MeshServer=local`** vào file cấu hình agent
(`webserver.js` dòng 6231), buộc agent **tự dò server bằng broadcast trên LAN**. Máy khác LAN —
laptop kết nối qua tailnet — sẽ không bao giờ tìm thấy, và giao diện agent hiện
`Server URL: local` mà không kết nối được.

Bỏ `LANonly` thì agent nhận thẳng `wss://<tên-server>:4430/agent.ashx`. **Vẫn không lộ ra
Internet** vì `portbind` chỉ nghe trên tailnet.

Nhưng bỏ nó kéo theo hai thứ phải tắt:

```json
"webrtcconfig": { "iceServers": [] },
"mpsport": 0,
"amtmanager": false,
```

- **`webrtcconfig` rỗng** — mặc định MeshCentral thêm STUN của Google/Cloudflare. §4 cấm đưa hệ
  thống ra dịch vụ ngoài.
- **`mpsport: 0` + `amtmanager: false`** — bỏ `LANonly` thì nó mở thêm cổng 4433 cho Intel AMT, và
  cổng đó nghe trên **mọi địa chỉ** (`::`) chứ không theo `portbind`. Đã kiểm chứng bằng
  `Get-NetTCPConnection`. Không dùng AMT nên tắt hẳn.

Sau khi tắt, chỉ còn 4430 và 8008, cả hai trên tailnet.

## Lỗi "Invalid origin in HTTP request"

MeshCentral so `Origin` của request với **CommonName của chứng chỉ**. Vào bằng **IP** thì không khớp
tên và bị từ chối.

Khai `allowedorigin` trong `domains` (khác `allowedFramingOrigins` — cái đó cho iframe, cái này cho
chính request):

```json
"domains": {
  "": {
    "allowedorigin": [
      "hub.tailnet-example.ts.net",
      "100.100.100.100",
      "localhost"
    ]
  }
}
```

Liệt kê mọi tên/IP dùng để truy cập. Thiếu cái nào thì vào bằng cái đó sẽ lỗi.

## Quyền — bắt buộc theo §5a

§5a cấm tuyệt đối chạy lệnh tuỳ ý:

> **Không thêm endpoint chạy lệnh tuỳ ý vào hệ thống này.** [...] Một phiên bị chiếm mà có quyền
> chạy lệnh tuỳ ý là mất trắng cả PC lẫn laptop.

MeshCentral mặc định **có** terminal/SSH — đúng thứ §5a cấm tuyệt đối.

### Điểm quan trọng: `--noterminal` KHÔNG áp cho tài khoản admin

Quyền `noterminal` là quyền **của một người dùng trên một group**, không phải thuộc tính của group.
Và tài khoản tạo group là chủ group, có toàn quyền — `AddUserToDeviceGroup` với chính mình trả
`Can't change self, Nothing done`.

**Nên cần hai tài khoản:**

| Tài khoản | Vai trò | Terminal |
|---|---|---|
| `toikobi401` | Quản trị — chỉ dùng khi cấu hình | Có (không chặn được) |
| `hubuser` | **Dùng hàng ngày**, đăng nhập trong hub | Đã chặn |

```powershell
cd D:\App\MeshCentral
$env:NODE_TLS_REJECT_UNAUTHORIZED = "0"
$mc = "wss://100.100.100.100:4430"

# 1. Tạo group (lệnh này KHÔNG nhận --noterminal)
node node_modules/meshcentral/meshctrl.js AddDeviceGroup `
  --url $mc --name "May nha" `
  --loginuser <admin> --loginpass "<mật khẩu admin>"

# 2. Tạo tài khoản dùng hàng ngày
node node_modules/meshcentral/meshctrl.js AddUser `
  --url $mc --user hubuser --pass "<mật khẩu riêng>" `
  --loginuser <admin> --loginpass "<mật khẩu admin>"

# 3. Gán vào group, chặn terminal
node node_modules/meshcentral/meshctrl.js AddUserToDeviceGroup `
  --url $mc --group "May nha" --userid hubuser `
  --remotecontrol --wakedevices --managedevices `
  --noterminal --nofiles --noregistry `
  --loginuser <admin> --loginpass "<mật khẩu admin>"
```

⚠️ **`AddDeviceGroup` không nhận `--noterminal`** — đưa vào thì bị **bỏ qua im lặng**, group vẫn bật
terminal. Cờ quyền chỉ có tác dụng ở `AddUserToDeviceGroup`.

Các cờ quyền (kiểm chứng bằng `meshctrl help AddUserToDeviceGroup`):

| Cờ | Tác dụng |
|---|---|
| `--noterminal` | **Ẩn tab terminal — bắt buộc theo §5a** |
| `--nofiles` | Ẩn tab truyền file |
| `--noregistry` | Ẩn tab registry |
| `--remotecontrol` | Cho điều khiển màn hình |
| `--wakedevices` | Cho đánh thức máy |
| `--desktopviewonly` | Chỉ xem màn hình, không điều khiển |

**Mật khẩu có ký tự đặc biệt** (`@`, `!`, `$`) phải đặt trong nháy kép ở PowerShell.

**Cách kiểm chứng:** đăng nhập bằng `hubuser`, chọn một máy — không được thấy tab *Terminal*.

## Cài agent lên từng máy

Trong giao diện MeshCentral: chọn device group → **Add Agent** → tải installer → chạy trên máy đích.

Khác với agent tự viết trước đây, bản này tự cài làm dịch vụ và khởi động cùng máy — không phải mở
cửa sổ terminal và để đó.

## Điều khiển nguồn từ dòng lệnh

```bash
node node_modules/meshcentral/meshctrl.js DevicePower --wake  --id "<deviceid>"
node node_modules/meshcentral/meshctrl.js DevicePower --sleep --id "<deviceid>"
node node_modules/meshcentral/meshctrl.js DevicePower --reset --id "<deviceid>"
node node_modules/meshcentral/meshctrl.js DevicePower --off   --id "<deviceid>"
```

Thêm `--json` để lấy kết quả dạng JSON.

## Còn lại — chưa làm

- **Nhật ký kiểm toán §5a điều 7** (ai tắt máy lúc nào) hiện nằm trong MeshCentral, không nằm trong
  DB của hub. Cần quyết định: đọc lại từ MeshCentral, hay giữ nhật ký riêng.
- **Wake vẫn cần đo phần cứng** (§12 câu 10) — MeshCentral gửi được magic packet, nhưng BIOS/driver
  không hỗ trợ thì vẫn không đánh thức được.
- Mã điều khiển nguồn tự viết trước đây còn trong repo, đã đánh dấu `ĐÃ THAY THẾ`. Xoá khi
  MeshCentral chạy ổn định.
