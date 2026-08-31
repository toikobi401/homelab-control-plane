# scripts/

## `hub_device.py` — quản lý thiết bị từ dòng lệnh

Đăng ký máy vào hub, xem danh sách, duyệt, thu hồi duyệt, gỡ khỏi sổ.

Chỉ dùng **thư viện chuẩn của Python** — máy mới chỉ cần cài Python, không phải `pip install` gì.
Cần Python 3.10 trở lên.

### Đăng ký một máy mới

Trên máy cần thêm vào hệ thống:

```bash
python hub_device.py register --hub https://<ip-tailnet-của-hub>:7189
```

Script tự dò và gửi lên hub:

| Thông tin | Cách dò |
|---|---|
| Địa chỉ tailnet | Hỏi `tailscale ip -4`; không có thì quét card mạng trong dải `100.64.0.0/10` |
| MAC | `uuid.getnode()`, bỏ qua giá trị ngẫu nhiên khi không đọc được MAC thật |
| Nhãn LAN | Subnet của card **vật lý**, đã loại VirtualBox/VMware/WSL/Radmin/Tailscale |
| Tên máy, OS | `socket.gethostname()`, `platform.system()` |

Xem trước mà chưa gửi gì:

```bash
python hub_device.py register --hub ... --dry-run
python hub_device.py detect          # chỉ in thông tin dò được, không gọi hub
```

**Máy đang chạy hub** phải thêm `--backend-host`. Khai sai chỗ này thì §5a điều 5 mất tác dụng —
hub sẽ cho phép tự tắt chính nó.

Sau khi đăng ký, thiết bị ở trạng thái **chờ duyệt** và chưa nhận được lệnh (§5a).

### Các lệnh khác

```bash
python hub_device.py list                              # danh sách + trạng thái duyệt
python hub_device.py approve    --hostname LAPTOP-ABC  # duyệt
python hub_device.py revoke     --hostname LAPTOP-ABC  # thu hồi duyệt (đảo ngược được)
python hub_device.py unregister --hostname LAPTOP-ABC  # gỡ hẳn khỏi sổ
```

Bỏ `--hostname` thì mặc định là **máy hiện tại**. Trùng tên máy thì dùng `--id`.

**`revoke` khác `unregister`:**

- `revoke` — thiết bị vẫn trong sổ, chỉ là không nhận lệnh nữa. Duyệt lại bất cứ lúc nào.
- `unregister` — gỡ hẳn. Máy đó phải đăng ký lại và **chờ duyệt lại từ đầu**.
  Nhật ký kiểm toán cũ **vẫn giữ nguyên** (§5a điều 7 — nó đã chép sẵn tên máy).

Cả hai đều hỏi xác nhận; thêm `--yes` để bỏ qua (dùng trong script tự động).

### Xác thực

Hai loại, tuỳ lệnh — đúng theo mô hình bảo mật hiện tại:

| Lệnh | Cần gì | Lấy từ đâu |
|---|---|---|
| `register` | Khoá chung với agent | `--secret`, biến `HUB_AGENT_SECRET`, hoặc script hỏi |
| `list`, `approve`, `revoke`, `unregister` | Mật khẩu hub | `--password`, biến `HUB_PASSWORD`, hoặc script hỏi |
| `detect` | không | — |

Script tự lấy CSRF token và tự lấy lại khi hết hiệu lực.

Không truyền qua tham số thì script hỏi và **không hiện lúc gõ**. Truyền qua dòng lệnh thì mật khẩu
lọt vào lịch sử shell — chỉ nên dùng biến môi trường hoặc để script hỏi.

### Biến môi trường

```bash
export HUB_URL=https://100.100.100.100:7189
export HUB_AGENT_SECRET=...     # cho register
export HUB_PASSWORD=...         # cho các lệnh còn lại
```

### Chứng chỉ tự ký

Lúc phát triển backend dùng chứng chỉ của `dotnet dev-certs`, Python sẽ từ chối. Thêm `--insecure`:

```bash
python hub_device.py list --hub https://localhost:7189 --insecure
```

Khi đã chạy `tailscale cert` thì **bỏ cờ này đi** — đừng để nó thành thói quen.

### Ghi chú

- Cờ `--hub`, `--insecure`, `--yes` đặt **trước hay sau** tên lệnh đều được.
- Mã thoát: `0` thành công, `1` lỗi, `130` bị Ctrl+C. Lỗi in ra **stderr**.
