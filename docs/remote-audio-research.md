# Âm thanh khi điều khiển màn hình từ xa — kết quả khảo sát

**Ngày khảo sát:** 2026-09-01
**Trạng thái:** Chưa làm. Đã loại ba hướng, còn một hướng khả thi nhưng có cái giá đáng cân nhắc.

**Nhu cầu:** remote vào desktop thì nghe được âm thanh của máy đó, như Chrome Remote Desktop.

---

## Kết luận ngắn

Không có giải pháp nào vừa **miễn phí**, vừa **chỉ dùng trình duyệt** (§1), vừa **không phải cài
thêm driver**. Cả bốn hướng đã thử đều vướng ít nhất một điều.

| Hướng | Vướng ở đâu | Còn dùng được? |
|---|---|---|
| MeshCentral (đang dùng) | Giao thức KVM **không có lệnh audio** | ❌ Không thể |
| RDP + Guacamole | Windows 11 **Home** không có RDP server | ❌ Không thể |
| RustDesk | Có audio thật, nhưng **web client là bản trả phí** | ⚠️ Chỉ khi chấp nhận app native |
| VB-Cable + FFmpeg tự stream | Cần cài driver mỗi máy đích, tiếng lệch hình | ✅ Khả thi, có cái giá |

---

## 1. MeshCentral không truyền được âm thanh

**Đã kiểm chứng trong mã nguồn**, không phải đoán. Liệt kê toàn bộ lệnh của giao thức desktop
(`meshdesktopmultiplex.js`):

```
MNG_KVM_COMPRESSION    MNG_KVM_KEY
MNG_KVM_CONNECTCOUNT   MNG_KVM_KEYSTATE
MNG_KVM_COPY           MNG_KVM_MESSAGE
MNG_KVM_DISCONNECT     MNG_KVM_MOUSE
MNG_KVM_DISPLAY_INFO   MNG_KVM_MOUSE_CURSOR
MNG_KVM_FRAME_RATE_TIMER
MNG_KVM_GET_DISPLAYS
MNG_KVM_INIT_TOUCH
MNG_KVM_INPUT_LOCK
```

Hình, chuột, bàn phím, clipboard — **không có lệnh nào cho audio**.

Thứ duy nhất liên quan âm thanh là `chimes.mp3`: tiếng báo thông báo của giao diện web. Micro/camera
có xuất hiện nhưng thuộc tính năng **chat**, không phải remote desktop.

Đã có người xin tính năng này từ 2020 ([#1709](https://github.com/Ylianst/MeshCentral/issues/1709),
[#1197](https://github.com/Ylianst/MeshCentral/issues/1197)); bản 1.2.5 hiện tại vẫn chưa có.

**Đây là giới hạn của công cụ, không phải lỗi cấu hình.** Không có gì để sửa.

---

## 2. RDP + Guacamole — bị loại vì Windows Home

Guacamole hỗ trợ âm thanh RDP tốt (bật sẵn mặc định). Nhưng:

```powershell
(Get-CimInstance Win32_OperatingSystem).Caption
# Microsoft Windows 11 Home
```

**Windows 11 Home không có RDP server** — Microsoft chỉ cho ở bản Pro trở lên. Máy có thể *kết nối
tới* RDP nhưng không *nhận* kết nối.

Muốn dùng hướng này phải nâng cấp lên Windows Pro trên mọi máy đích.

---

## 3. RustDesk — có audio thật, nhưng web client trả phí

**Audio có thật và đúng thứ cần.** Đọc `src/server/audio_service.rs`, nhánh Windows:

```rust
#[cfg(windows)]
fn get_device() -> ResultType<(Device, SupportedStreamConfig)> {
    ...
    let device = HOST
        .default_output_device()
        .with_context(|| "Failed to get default output device for loopback")?;
```

Nó lấy **thiết bị phát mặc định để loopback** — bắt đúng âm thanh đang ra loa, giống Chrome Remote
Desktop.

**Nhưng web client là tính năng của bản Pro.** Điều kiện license: `(users × 10) + devices ≥ 400` —
gói doanh nghiệp, không phải vài đô.

Bản OSS miễn phí chỉ có **app native**, va thẳng vào §1:

> **Không ứng dụng native.** Không Android app, không iOS app, không desktop app. Chỉ web. Đây là lý
> do tồn tại của cả kiến trúc này.

Dùng RustDesk OSS nghĩa là cài app trên **cả máy điều khiển lẫn máy đích** — iPhone cũng cần app
riêng.

---

## 4. VB-Cable + FFmpeg — khả thi, nhưng đọc kỹ cái giá

Hướng duy nhất giữ được "chỉ web" mà không tốn tiền. Chưa làm.

### Vì sao cần driver ảo

Windows tách hai loại thiết bị âm thanh:

- **Thiết bị thu** (micro) — ứng dụng đọc được
- **Thiết bị phát** (loa) — ứng dụng chỉ ghi vào, **không đọc ra được**

FFmpeg chỉ đọc được thiết bị thu. Chạy thử trên máy phát triển (PC `War_Machine_2`):

```
ffmpeg -list_devices true -f dshow -i dummy

"Microphone (4- Comica_EJoy Uni)" (audio)
"Microphone (USBAudio2.0)" (audio)
```

Chỉ hai micro. **Không có mục nào cho âm thanh đang phát ra loa** — đúng thứ cần nghe.

FFmpeg trên Windows chỉ có `dshow` và `openal`, **không có WASAPI loopback**. Đó là lý do cần driver
trung gian.

VB-Cable tạo một **cặp thiết bị ảo nối với nhau**:

```
Ứng dụng (YouTube, nhạc…)
   ↓ phát ra
[CABLE Input]  ──── nối bên trong driver ────  [CABLE Output]
 (loa ảo)                                       (micro ảo)
                                                    ↓ FFmpeg đọc được
                                                stream → trình duyệt
```

### Vì sao phải cài trên MỖI máy đích

Driver nằm trên **máy phát ra âm thanh**, không phải máy đang ngồi.

Remote vào laptop để nghe nhạc đang chạy ở đó → âm thanh sinh ra **trên laptop** → VB-Cable phải cài
**trên laptop**.

| Máy | Vai trò | Cần VB-Cable |
|---|---|---|
| PC `War_Machine_2` | Chạy hub, cũng là máy đích | **Có** |
| Laptop `War-Machine` | Máy đích | **Có** |
| iPhone / Android | Chỉ để xem | Không |

Hiện tại là **hai lần cài**. Mỗi máy Windows thêm vào sau này cũng phải cài — cài thủ công, cần
quyền admin và khởi động lại máy.

### Ba cái giá phải chấp nhận

**1. Âm thanh bị chuyển hướng khỏi loa thật.**

Để FFmpeg bắt được, phải đặt CABLE Input làm thiết bị phát mặc định. Lúc đó **loa thật của máy đó im
lặng** — ai đang ngồi trước máy sẽ không nghe gì.

Khắc phục được bằng "Listen to this device" của Windows hoặc VoiceMeeter, nhưng đó là thêm một lớp
cấu hình nữa trên mỗi máy.

**2. Tiếng lệch hình.**

Hình đi qua MeshCentral, tiếng đi qua đường riêng — **hai luồng độc lập, không có cơ chế đồng bộ**.
Xem video sẽ thấy lệch.

Đây là hạn chế cố hữu của việc tách hai luồng, **không sửa được bằng code**. Chrome Remote Desktop
không bị vì nó truyền cả hai trong một giao thức.

**3. Phải tự viết và tự bảo trì phần stream.**

FFmpeg bắt → mã hoá Opus → phát qua hub → trình duyệt phát. Ước lượng một lượt làm việc, cộng thêm
phần xử lý khi FFmpeg chết hoặc thiết bị đổi.

Điều này va vào §2.3 ("tái sử dụng, đừng phát minh lại") — nhưng ở đây không có công cụ nào để tái
sử dụng, nên tự viết là lựa chọn duy nhất còn lại.

### Nên thử driver trước khi viết code

Cài VB-Cable trên PC, đặt làm thiết bị mặc định, rồi chạy FFmpeg xem có bắt được âm thanh thật
không. Khoảng mười phút.

Nếu hai cái giá đầu không chấp nhận được thì không đáng viết phần stream.

---

## Vì sao Chrome Remote Desktop làm được

Google viết **driver âm thanh riêng** cài vào Windows, và truyền tiếng chung một giao thức với hình
nên không lệch. Đó là phần mềm đóng, không có bản mã nguồn mở tương đương chạy trong trình duyệt.

Mọi giải pháp mã nguồn mở đều cần một trong hai: **cài driver ảo**, hoặc **dùng app native**.

---

## Trạng thái hiện tại

**Chưa làm gì.** MeshCentral vẫn chạy bình thường cho phần điều khiển màn hình không tiếng.

Khi nào quyết định làm, ba hướng còn lại theo thứ tự ít rủi ro nhất:

1. **Chấp nhận không có âm thanh** — remote desktop vẫn dùng để thao tác được.
2. **VB-Cable + tự stream** — giữ "chỉ web", nhưng đọc kỹ ba cái giá ở trên.
3. **RustDesk OSS + app native** — âm thanh chạy ngay, không phải viết gì, nhưng phá §1.
