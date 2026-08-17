# CONTEXT.md — Personal Device Hub (Trung tâm thiết bị cá nhân)

> **Đọc file này trước khi làm bất cứ việc gì.** Nếu một yêu cầu mâu thuẫn với quy tắc ở đây, hãy dừng
> lại và hỏi thay vì tự quyết định. Nếu một quyết định trong file này hoá ra là sai, hãy đề xuất chỉnh
> sửa chính file này như một phần của thay đổi — không được lặng lẽ né tránh nó.

---

## 1. Dự án này là gì

Một **hub thiết bị cá nhân, tự host (self-hosted)** dành cho các thiết bị của riêng một người dùng.
Không phải sản phẩm thương mại, không đa người dùng (multi-tenant), không đưa lên Google Play. Ứng
dụng được cài theo kiểu sideload.

Hub kết nối: 1 điện thoại Android, 1 máy tính để bàn Windows, 1 laptop Windows, và về sau là một NAS
cá nhân.

Bốn năng lực, theo thứ tự xây dựng:

| # | Năng lực | Trạng thái |
|---|---|---|
| 1 | Theo dõi hiện diện + vị trí thiết bị (điện thoại và laptop) | Chưa bắt đầu |
| 2 | Duyệt và truyền file giữa các thiết bị | Chưa bắt đầu |
| 3 | Sao lưu lên cloud storage, sau đó lên NAS cá nhân | Chưa bắt đầu |
| 4 | Điều khiển màn hình từ xa từ điện thoại vào PC/laptop Windows | Chưa bắt đầu |

Các năng lực 2–4 phụ thuộc vào lớp transport của năng lực 1. Không được làm sai thứ tự.

### Những thứ dứt khoát không làm (non-goals)

- Không đa người dùng, không hệ thống tài khoản, không luồng đăng ký. Chỉ định danh theo thiết bị.
- Không làm việc tuân thủ chính sách Play Store. Ứng dụng được sideload; các hạn chế chính sách về
  vị trí nền và `MANAGE_EXTERNAL_STORAGE` không áp dụng. Không thêm các giải pháp vòng vo sinh ra do
  Play Store.
- Không dùng backend cloud mà người dùng phải trả tiền hoặc phải vận hành. Các thiết bị nói chuyện
  trực tiếp với nhau.
- Không có web frontend (có thể làm Desktop App). Chỉ có ứng dụng Android + desktop agent chạy nền (headless).

---

## 2. Quyết định kiến trúc quan trọng nhất

**Chúng ta không tự xây NAT traversal, định danh thiết bị, hay mã hoá transport.**

Tất cả thiết bị tham gia một **Tailscale tailnet** (lưới WireGuard). Mỗi thiết bị nhận một địa chỉ
`100.x.y.z` ổn định, truy cập được từ bất kỳ đâu. Điều này cho chúng ta, miễn phí:

- Vượt NAT/firewall mà không cần port forwarding hay relay server do ta vận hành
- Xác thực lẫn nhau và mã hoá đầu-cuối
- Địa chỉ ổn định và danh sách thiết bị

Mọi thứ ta xây đều giả định **"tất cả thiết bị của tôi nằm trên cùng một mạng LAN phẳng, tin cậy, đã
mã hoá."** Giả định đó loại bỏ khoảng 70% phần việc khó của dự án này.

Nếu Tailscale trở nên không chấp nhận được, phương án dự phòng là tự host Headscale (cùng client,
control plane riêng). Không đề xuất cấu hình WireGuard thuần, hole-punching tự viết, hay relay server
công khai.

### Hệ quả: tái sử dụng giao thức, đừng phát minh lại

| Nhu cầu | Dùng | KHÔNG được |
|---|---|---|
| Truyền file | SFTP qua tailnet (`sshd` trên máy desktop) | Viết giao thức truyền file riêng |
| Đồng bộ file (về sau) | Syncthing, điều khiển bởi UI của ta | Tự viết lại đồng bộ mức block |
| Sao lưu cloud/NAS | Gọi binary `rclone` từ desktop agent | Tự tay viết client S3/Drive/WebDAV |
| Điều khiển từ xa | Nhúng thư viện client VNC/RDP có sẵn | Viết codec hay giao thức nhập liệu |

Ứng dụng của ta là một **control plane và một UI**. Phần việc nặng được giao cho các công cụ đã được
kiểm chứng. Bất kỳ PR nào bắt đầu viết lại một mục ở cột bên phải đều bị từ chối.

---

## 3. Stack — đã chốt, không bàn lại

### Ứng dụng Android

- Kotlin, chỉ **Jetpack Compose**. Không XML layout, không Fragment, không View system.
- `minSdk 29`, `targetSdk` = bản ổn định mới nhất.
- Single Activity, Navigation Compose (route type-safe).
- **Hilt** cho DI. **Room** cho DB cục bộ. **DataStore (Proto)** cho settings.
- Coroutines + Flow ở mọi nơi. Không RxJava, không `LiveData`, không callback trong code mới.
- **WorkManager** cho mọi thứ định kỳ hoặc có thể hoãn.
- **Ktor client** cho HTTP. Không dùng Retrofit — ta dùng chung client với desktop agent.
- **kotlinx.serialization** cho mọi định dạng truyền tải. Không Gson, không Moshi.
- Version catalog (`libs.versions.toml`) cho mọi dependency. Không hardcode version trong
  `build.gradle.kts`.

### Desktop agent (PC Windows + laptop)

- Service Kotlin/JVM chạy nền, **Ktor server**, đóng gói bằng `jpackage` và cài như một Windows
  service qua NSSM hoặc `sc.exe`.
- Lý do: một ngôn ngữ cho toàn dự án. Người dùng đang học Android; thêm ngôn ngữ thứ hai (Go/Rust)
  làm chi phí chuyển ngữ cảnh tăng gấp đôi để đổi lấy lợi ích không đáng kể.
- Module Kotlin Multiplatform dùng chung `:shared` chứa DTO, hằng số giao thức, và validation. Cả
  Android lẫn desktop đều phụ thuộc vào nó. Không nhân bản model class.

### Bố cục repo

```
/app                    Module ứng dụng Android
/core/designsystem      Compose theme, tokens, component dùng chung
/core/data              Repository, Room, DataStore
/core/network           Ktor client, khám phá thiết bị trên tailnet
/core/common            Result type, dispatcher, extension
/feature/devices        Danh sách thiết bị, hiện diện, bản đồ vị trí
/feature/files          Trình duyệt file, hàng đợi truyền file
/feature/backup         Job sao lưu và lịch sử
/feature/remote         Phiên điều khiển màn hình từ xa
/shared                 Module KMP: DTO, giao thức, validation
/desktop-agent          Service Ktor Kotlin/JVM cho Windows
```

Module feature phụ thuộc vào `core`. Module `core` không bao giờ phụ thuộc vào `feature`. Không gì
phụ thuộc vào `:app`.

---

## 4. Những ràng buộc Android mà bạn CHẮC CHẮN sẽ làm sai — đọc kỹ

Đây là những chỗ mà các agent liên tục sinh ra code hỏng. Hãy kiểm chứng với tài liệu hiện hành trên
`developer.android.com` trước khi viết; đừng dựa vào trí nhớ từ dữ liệu huấn luyện.

### Vị trí chạy nền

1. Quyền runtime phải được xin theo **hai bước riêng biệt**: trước hết `ACCESS_FINE_LOCATION`
   (foreground), rồi `ACCESS_BACKGROUND_LOCATION` trong một lần xin **sau, tách rời**. Xin cả hai
   cùng lúc sẽ thất bại âm thầm.
2. Theo dõi vị trí nền đòi hỏi một **foreground service** khai báo với
   `android:foregroundServiceType="location"` trong manifest, cộng với quyền
   `FOREGROUND_SERVICE_LOCATION` (Android 14+).
3. Android 12+ chặn việc khởi động foreground service từ nền, trừ một tập các trường hợp miễn trừ
   đã định nghĩa. Hãy kích hoạt service từ hành động của người dùng hoặc từ một đường miễn trừ được
   cho phép.
4. Theo dõi dài hạn đáng tin cậy cần **miễn trừ tối ưu hoá pin**
   (`REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`), xin một cách tường minh kèm giải thích rõ ràng trong
   ứng dụng.
5. Dùng **`FusedLocationProviderClient`** với một request cân bằng năng lượng. Không bao giờ poll GPS
   trực tiếp. Nhịp mặc định: 15 phút, `PRIORITY_BALANCED_POWER_ACCURACY`. Cho phép cấu hình được.
6. Thêm một thông báo thường trực, dễ thấy và một công tắc tắt (kill switch) rõ ràng trong ứng dụng.
   Người dùng phải luôn nhìn thấy và dừng được việc theo dõi.

### Lưu trữ

- Ứng dụng được sideload, nên `MANAGE_EXTERNAL_STORAGE` là chấp nhận được và là lựa chọn thực dụng
  cho một trình quản lý file. Xin quyền qua
  `Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION`.
- Vẫn dùng các API scoped-storage (`MediaStore`, SAF) ở những chỗ chúng đủ dùng. Quyền truy cập rộng
  là phương án dự phòng, không phải đường đi mặc định.
- Không bao giờ ghi vào đường dẫn hardcode. Không bao giờ giả định có `/sdcard`.

### Vị trí trên desktop

Laptop không có GPS. Hãy báo cáo vị trí **thô (coarse)**: SSID/BSSID Wi-Fi gần nhất cộng với định vị
theo IP, làm mới khi mạng thay đổi. Ghi nhãn rõ ràng là "gần đúng" trong UI. Không giả vờ độ chính
xác cao.

---

## 5. Yêu cầu bảo mật — không thương lượng

Ứng dụng này gom lịch sử vị trí, nội dung file, và quyền truy cập từ xa vào PC của người dùng. Một
chiếc điện thoại bị xâm nhập nghĩa là mất trắng toàn bộ. Người dùng đã từng mất một điện thoại; hãy
giả định điều đó sẽ lại xảy ra.

1. **Tuyệt đối không để secret trong source.** Không API key, không token, không tailnet auth key,
   không thông tin đăng nhập cloud trong repo hay trong `gradle.properties` được track. Dùng
   `local.properties` (đã gitignore) và Android Keystore lúc runtime.
2. **Thông tin xác thực khi lưu trữ** phải nằm trong `EncryptedSharedPreferences` hoặc các cột Room
   được bọc bằng Keystore. Không bao giờ để plaintext, không bao giờ để trong DataStore không mã hoá.
3. **Cổng sinh trắc học/PIN** bắt buộc trước khi: mở phiên điều khiển từ xa, xem/sửa thông tin xác
   thực, và xem lịch sử vị trí. Dùng `BiometricPrompt`, có fallback bằng device credential.
4. **Đường xoá từ xa (remote wipe).** Desktop agent phải có khả năng thu hồi tailnet key của một
   thiết bị và vô hiệu hoá thông tin xác thực đã lưu trên đó. Xây dựng ngay ở phase 1, không để sau.
5. **Lịch sử vị trí chỉ lưu cục bộ** theo mặc định, có giới hạn thời gian lưu (mặc định 30 ngày) và
   một nút xoá sạch một chạm. Không bao giờ đồng bộ nó lên cloud storage.
6. Đích sao lưu phải lưu các blob **đã mã hoá phía client**. Dùng remote `crypt` của rclone. Nhà cung
   cấp cloud không bao giờ được thấy plaintext.
7. Không log bất cứ thứ gì nhạy cảm. Không toạ độ, không đường dẫn file, không token trong logcat —
   kể cả trong bản debug.

---

## 6. Quy ước viết code

- Code style **Kotlin official**. `ktlint` + `detekt` chạy trong CI và phải pass.
- Xử lý kết quả: sealed `Result<T>` trong `:core:common`. Không ném exception qua ranh giới module,
  không dùng nullable-thay-cho-lỗi.
- ViewModel phơi ra một `StateFlow<UiState>` duy nhất. UI là một hàm thuần của state. Không có
  business logic trong composable.
- Composable: hoist state, không truyền `ViewModel` xuống dưới composable cấp màn hình, có `@Preview`
  cho mọi component tái sử dụng được.
- Mỗi module mới đều phải có test. `kotlin.test` + Turbine cho Flow, Robolectric chỉ khi không thể
  tránh.
- Hàm public trong `:shared` cần KDoc. Code private không cần comment giải thích *cái gì* — chỉ giải
  thích *tại sao*, khi điều đó không hiển nhiên.
- Commit message: Conventional Commits (`feat:`, `fix:`, `refactor:`, `chore:`).

---

## 7. Build và kiểm chứng

Android CLI đã được cài và là điểm vào ưu tiên hơn Gradle thuần — nó cho output có cấu trúc và phân
tích ngữ nghĩa.

```bash
android build                      # build dự án
android run --device <id>          # cài đặt và chạy
android test                       # unit test
android studio lint                # lint qua engine phân tích của IDE
android studio preview <file>      # render Compose preview
android devices                    # liệt kê thiết bị/emulator đang kết nối
```

**Định nghĩa "hoàn thành" cho mọi thay đổi:**

1. `android build` thành công với zero warning mới.
2. `android studio lint` không báo issue mới.
3. Test pass, bao gồm test mới cho logic mới.
4. Thay đổi liên quan Compose đã được render qua `android studio preview` và kiểm tra bằng mắt.
5. Nếu thay đổi động tới quyền, lưu trữ, hoặc mạng: phải kiểm chứng thủ công trên **thiết bị thật**,
   không chỉ trên emulator. Hành vi vị trí và mạng của emulator khác biệt đáng kể.

Không bao giờ đánh dấu một task là xong chỉ vì "nó compile được".

---

## 8. Cách làm việc trên dự án này

- **Hỏi trước khi scaffold.** Trước khi tạo hơn ba file mới, hãy trình bày kế hoạch và chờ.
- **Mỗi lần một năng lực.** Không bắt đầu năng lực *n+1* khi *n* chưa xong. Không thêm các refactor
  kiểu "tiện tay làm luôn".
- **Không có phần triển khai giả.** Không `TODO()`, không hàm stub trả về dữ liệu giả, không màn hình
  mock được trình bày như đã chạy được. Nếu chưa xây được thứ gì đó, hãy nói ra và dừng lại.
- **Không thêm dependency mới mà không hỏi.** Nêu rõ nó làm gì, kích thước, tình trạng bảo trì, và nó
  thay thế cho cái gì.
- **Khi tài liệu và trí nhớ của bạn mâu thuẫn, tài liệu thắng.** Các API Android trong mảng này
  (foreground service, lưu trữ, quyền) đã thay đổi đáng kể qua Android 12–16. Hãy tra cứu.
- **Nói cho người dùng biết khi họ sai.** Nếu một hướng tiếp cận được yêu cầu là ý tồi, hãy nói thẳng
  và giải thích tại sao trước khi triển khai. Không xây thứ mà bạn tin là hỏng.
- Ưu tiên xoá code hơn thêm cờ (flag). Ưu tiên giải pháp nhàm chán.

---

## 9. Giai đoạn hiện tại

**Phase 0 — Nền móng. Chưa xây gì cả.**

Tiêu chí hoàn tất Phase 0:

- [ ] Tailscale đã cài và kiểm chứng trên điện thoại, PC, và laptop; cả ba ping được nhau
- [ ] Đã tạo dự án Gradle với bố cục module theo §3
- [ ] Đã cấu hình version catalog, ktlint, detekt; `android build` xanh
- [ ] Module `:shared` với `Device`, `DeviceStatus`, và hằng số phiên bản giao thức
- [ ] Khung desktop agent: Ktor server trên địa chỉ tailnet, một endpoint `/health`
- [ ] Khung ứng dụng Android: single Activity, Compose theme, màn hình danh sách thiết bị rỗng
- [ ] Ứng dụng Android gọi được `/health` của desktop agent qua tailnet và hiển thị kết quả

Không bắt đầu theo dõi vị trí cho đến khi mọi ô ở trên đã được tick.

---

## 10. Nhật ký quyết định

Ghi thêm vào đây mỗi khi một quyết định kiến trúc được đưa ra hoặc bị đảo ngược. Ngày, quyết định,
lý do.

| Ngày | Quyết định | Lý do |
|---|---|---|
| 2026-08-18 | Tailscale làm lớp transport | Loại NAT traversal, định danh, và mã hoá ra khỏi phạm vi |
| 2026-08-18 | Desktop agent bằng Kotlin/JVM, không dùng Go | Một ngôn ngữ; lập trình viên đơn độc; giảm chuyển ngữ cảnh |
| 2026-08-18 | Giao cho rclone / Syncthing / VNC thay vì tự viết lại | Kiểm soát phạm vi; đây là các bài toán đã được giải |
| 2026-08-18 | Chỉ sideload, không lên Play Store | Mở khoá vị trí nền và truy cập file rộng mà không cần lách chính sách |

---

## 11. Câu hỏi mở — không đoán, hãy hỏi

1. Dùng nhà cung cấp cloud nào để sao lưu? Ảnh hưởng tới cấu hình remote của rclone, không ảnh hưởng
   kiến trúc.
2. Model và giao thức NAS (SMB / NFS / SFTP)? Hoãn cho tới khi có NAS.
3. Điều khiển từ xa: RDP (có sẵn trên Windows Pro, cần bản Pro) hay VNC (chạy được trên bản Home, cần
   cài server)? Quyết định khi bắt đầu năng lực 4 — kiểm tra trước xem mỗi máy chạy bản Windows nào.
4. Lịch sử vị trí nên xem được dưới dạng bản đồ, hay "vị trí cuối + timestamp" là đủ? Bản đồ thêm một
   dependency Google Maps SDK và một API key.
