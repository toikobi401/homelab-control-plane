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

Hub cần **hai** địa chỉ, không phải một:

```bash
# Địa chỉ trong tailnet — dùng khi mở hub qua MagicDNS hoặc 100.x
dotnet user-secrets --project backend/Hub.Api set "MeshCentral:Url" "https://hub.tailnet-example.ts.net:4430"

# Địa chỉ công khai — dùng khi mở hub qua domain Internet
dotnet user-secrets --project backend/Hub.Api set "MeshCentral:PublicUrl" "https://mesh.youtubecontentgen.io.vn"
```

Biến môi trường khi chạy thật: `MeshCentral__Url` và `MeshCentral__PublicUrl`.

### Vì sao phải khai cả hai

Tên MagicDNS (`*.ts.net`) **chỉ phân giải được từ thiết bị đã cài Tailscale**. Mở hub qua
domain công khai rồi nhúng địa chỉ tailnet thì trình duyệt báo:

> Không thể tìm thấy địa chỉ IP của máy chủ hub.tailnet-example.ts.net.

Đã gặp thật trên `hub.youtubecontentgen.io.vn/remote`. Backend chọn địa chỉ theo **Host của
request** (`MeshCentralOptions.ResolveUrl`): vào qua `.ts.net`/`100.x`/`localhost` thì dùng
`Url`, vào qua đường nào khác thì dùng `PublicUrl`. Thiếu cái nào thì rơi về cái còn lại.

Dùng Host chứ không dùng IP client vì sau Cloudflare Tunnel mọi request đều đến từ loopback —
IP không phân biệt được lối vào, còn Host thì giữ nguyên cái người dùng gõ.

### Địa chỉ công khai còn giải quyết chuyện chứng chỉ

Chứng chỉ tự ký của MeshCentral khiến trình duyệt chặn iframe **mà không hỏi gì** — chỉ hiện
khung trắng. Domain công khai đi qua Cloudflare nên có chứng chỉ hợp lệ, không dính lỗi đó.

Dù dùng địa chỉ nào, MeshCentral vẫn phải khai `frame-ancestors` cho origin của hub — xem
mục CSP bên dưới.

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

**Lỗi hiện ở đâu:** kiểm tra origin nằm ở WebSocket `control.ashx` (`webserver.js` dòng 7401), tức
là **sau khi** trang login đã tải xong. Nên triệu chứng là trang hiện ra bình thường rồi mới báo
"Invalid origin in HTTP request, click to reconnect" — dễ tưởng lỗi mạng.

**MeshCentral chỉ đọc config lúc khởi động.** Sửa `allowedorigin` xong phải khởi động lại; không thì
nó vẫn chạy bản cũ và lỗi y nguyên. Đã mất thời gian vì chuyện này — kiểm tra bằng cách so
`CreationDate` của tiến trình `node.exe` với `LastWriteTime` của `config.json`.

Khi thêm tên miền mới (Cloudflare Tunnel), phải thêm vào **cả hai** danh sách:

```json
"settings": {
  "allowedFramingOrigins": ["https://hub.tenmien-cua-ban.com", ...]
},
"domains": {
  "": { "allowedorigin": ["mesh.tenmien-cua-ban.com", ...] }
}
```

`allowedFramingOrigins` cho iframe của hub; `allowedorigin` cho chính request tới MeshCentral. Thiếu
cái nào hỏng cái đó.

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

## Máy NGOÀI tailnet: phải khai `agentaliasdns`

Mặc định MeshCentral ghi vào agent địa chỉ lấy từ khoá `cert` — tức **tên tailnet**
(`wss://hub.tailnet-example.ts.net:4430/agent.ashx`).

Tên MagicDNS **chỉ phân giải được từ máy đã cài Tailscale**. Máy ngoài tailnet cài agent xong sẽ
thấy `Current Agent Status: RUNNING` nhưng **không bao giờ xuất hiện trong danh sách** — nó không
tìm thấy server để kết nối.

Triệu chứng dễ nhầm: agent báo RUNNING, Server URL trông đúng, không có lỗi nào hiện ra.

Sửa bằng cách khai tên miền công khai cho agent:

```json
"agentaliasdns": "mesh.tenmien-cua-ban.com",
"agentaliasport": 443
```

- **`agentaliasdns`** — tên agent sẽ gọi tới, thay cho tên tailnet.
- **`agentaliasport`: 443** chứ không phải 4430. Qua Cloudflare Tunnel thì client gọi cổng HTTPS
  chuẩn; tunnel mới chuyển tiếp về 4430 bên trong.

Máy **trong** tailnet vẫn dùng được bình thường — tunnel trỏ về chính cổng agent đang nghe.

⚠️ **Phải tải lại agent sau khi đổi.** File cài đặt nhúng địa chỉ server vào bên trong, nên bản đã
tải trước đó vẫn mang tên cũ. MeshCentral ký lại các file agent lúc khởi động (mất ~30 giây).

## Hai đường kết nối: tailnet và Cloudflare

Hub có hai lối vào MeshCentral, và agent nên đi lối gần nhất:

| Máy | Đường | Vì sao |
|---|---|---|
| Đã vào tailnet | `wss://hub.tailnet-example.ts.net:4430` | Đường thẳng, không ra Internet |
| Chưa vào tailnet | `wss://mesh.tenmien-cua-ban.com:443` | Lối duy nhất tới được |

MeshCentral **chỉ sinh được một địa chỉ** cho mọi agent — địa chỉ công khai
(`agentaliasdns`). Máy trong tailnet vì thế vẫn đi vòng ra Internet rồi quay lại.

### Cách dùng

```powershell
.\scripts\mesh-agent-route.ps1 detect   # xem máy này nên đi đường nào
.\scripts\mesh-agent-route.ps1 apply    # ghi vào .msh (cần Administrator)
.\scripts\mesh-agent-route.ps1 status   # đang trỏ đâu
```

Chạy `apply` **sau khi cài agent**, và chạy lại mỗi khi máy đổi mạng (vào tailnet
lần đầu, hoặc rời tailnet). Địa chỉ đã ghi là cố định cho tới lần chạy sau.

Ép một đường cụ thể khi cần:

```powershell
.\scripts\mesh-agent-route.ps1 apply -Force cloudflare
```

### Vì sao dò bằng kết nối thật, không chỉ hỏi `tailscale status`

Script kiểm tra ba bước và dừng ngay khi bước nào hỏng:

1. Tailscale đang chạy chưa
2. Tên MagicDNS phân giải được không
3. **Cổng 4430 có thật sự mở không**

Bước 3 là bước quan trọng. Tailscale chạy và DNS phân giải được vẫn có thể không
tới nơi — server tắt, ACL chặn, máy vừa đổi mạng. Tin vào trạng thái thay vì kết
quả sẽ khoá máy vào một đường chết, mà agent thì **im lặng không báo gì**.

### Vì sao KHÔNG khai cả hai địa chỉ

MeshAgent có nhận danh sách ngăn cách bằng dấu phẩy:

```
MeshServer=wss://a...,wss://b...
ServerID=<hash-a>,<hash-b>
```

Nhưng nó **bốc ngẫu nhiên**, không ưu tiên — `agentcore.c` dòng 3894:

```c
util_random(4, (char*)&rval);
agent->serverIndex = (rval % rs->NumResults) + 1;
```

Máy trong tailnet vẫn qua Cloudflare khoảng một nửa số lần. Đó là cân bằng tải,
không phải "ưu tiên đường gần". Thêm nữa `ServerID` phải khai đúng số phần tử,
lệch là agent bỏ kết nối với `ServerID Count Mismatch`.

Cơ chế này cũng **không có trong tài liệu chính thức** — MeshAgent readme không
liệt kê `MeshServer` trong bảng thiết lập, và các yêu cầu tính năng failover
([#5831](https://github.com/Ylianst/MeshCentral/issues/5831),
[#3208](https://github.com/Ylianst/MeshCentral/issues/3208)) đều bị đóng "not
planned". Dựa vào hành vi không được ghi nhận là chấp nhận rủi ro nó biến mất sau
một bản cập nhật.

⚠️ **`agentupdate` ghi đè `.msh`** bằng bản nhúng trong exe. Sau khi cập nhật
agent, chạy lại `apply`.

## Lỗi "Agent bad web cert hash" — agent RUNNING nhưng máy không hiện

Sau khi khai `agentaliasdns`, agent kết nối được nhưng vẫn **không xuất hiện trong danh sách**. Log
server (không phải log agent) cho biết lý do:

```
Agent bad web cert hash (Agent:4b49221c5d != Server:790f626271 or 8d1e621958), holding connection
```

Agent kiểm tra hash chứng chỉ của server để **chống man-in-the-middle**. Qua Cloudflare Tunnel, agent
thấy chứng chỉ của **Google Trust Services** (Cloudflare cấp), còn MeshCentral mong đợi chứng chỉ tự
sinh của chính nó — hai hash khác nhau nên nó giữ kết nối lại.

Kiểm chứng khác biệt:

```bash
# Qua tunnel — chứng chỉ Cloudflare
openssl s_client -connect mesh.tenmien-cua-ban.com:443 | openssl x509 -noout -issuer
# issuer=C=US, O=Google Trust Services, CN=WE1

# Qua tailnet — chứng chỉ MeshCentral tự sinh
openssl s_client -connect 100.100.100.100:4430 | openssl x509 -noout -issuer
# issuer=CN=MeshCentralRoot-8aeaa7
```

### Cách sửa: giới hạn theo IP, KHÔNG tắt hoàn toàn

```json
"ignoreagenthashcheck": ["100.100.100.100"]
```

Khoá này nhận **danh sách IP**, không chỉ `true`. Chỉ bỏ qua kiểm tra cho địa chỉ mà `cloudflared`
chuyển tiếp vào — mọi đường khác vẫn giữ nguyên lớp chống man-in-the-middle.

⚠️ **Không đặt `"ignoreagenthashcheck": true`.** Nó tắt kiểm tra cho **mọi** agent, kể cả kết nối
trực tiếp — bỏ hẳn một lớp bảo vệ để sửa một trường hợp.

### Đã thử và KHÔNG dùng được: `certurl`

MeshCentral có khoá `certurl` để khai chứng chỉ thật khi chạy sau proxy. Nhưng nó dùng
`domains[].dns` làm host để kiểm tra, mà khai `dns` ở domain mặc định lại đổi cách định tuyến của cả
server. Thử thì báo:

```
Failed to load web certificate at: "https://mesh...:443", host: "hub.tailnet-example.ts.net"
```

Nó gọi tên miền công khai nhưng kiểm tra host tailnet. `ignoreagenthashcheck` giới hạn IP đơn giản
hơn và không đụng vào định tuyến.

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
