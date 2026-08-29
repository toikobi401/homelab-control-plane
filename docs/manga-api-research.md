# Nghiên cứu API truyện tranh công khai — tài liệu áp dụng cho Năng lực 5

> **Trạng thái:** Đã xác minh bằng thực nghiệm ngày **2026-08-29** từ máy PC Windows của dự án
> (mạng gia đình tại Việt Nam). Mọi con số rate limit và luồng API trong file này lấy từ **tài liệu
> chính thức của MangaDex**, không lấy từ trí nhớ — đúng theo yêu cầu của CONTEXT.md §5 mục "Chưa
> xác minh" và §9 "tài liệu thắng trí nhớ".
>
> ### ⚠️ Bản sửa đổi lần 2 — cùng ngày 2026-08-29, buổi tối
>
> **Kết luận "MangaDex bị chặn hoàn toàn" của bản đầu là SAI.** Chặn chỉ áp dụng cho **TCP**.
> Toàn bộ API, ảnh bìa và ảnh trang đều **truy cập được bình thường** từ mạng gia đình VN nếu client
> đi bằng đúng giao thức. Đã tải thành công một ảnh trang thật 1.33 MB và một ảnh bìa 317 KB.
> Xem §1.3–§1.8. Các mục §2–§5 của bản đầu vẫn đúng và không đổi.
>
> **Nguồn tài liệu chính thức:** repo `mangadex-pub/mangadex-api-docs` trên GitLab
> (https://gitlab.com/mangadex-pub/mangadex-api-docs). Trang
> `https://api.mangadex.org/docs/swagger.html` **đã mở được trực tiếp** trong lần kiểm chứng thứ hai
> (Chromium) — spec sống hiện là `https://api.mangadex.org/docs/static/api.yaml`.

---

## 0. TL;DR — bốn kết luận quyết định kiến trúc

1. **Năng lực 5 KHẢ THI. Không cần VPN, không cần proxy, không cần sửa ranh giới §5 của CONTEXT.md.**
   Việc chặn của ISP chỉ tác động lên **TCP**; MangaDex quảng bá `alpn=h3` và **HTTP/3 qua QUIC
   (UDP 443) đi qua sạch**, 8/8 lần thử, độ trễ trung bình 75 ms. Xem §1.4, bằng chứng end-to-end ở §1.6.
2. **Ràng buộc kéo theo là ở tầng client, không ở tầng mạng: app Android bắt buộc phải có một HTTP
   client nói được HTTP/3.** Ktor + OkHttp (mặc định trên Android) **không** làm được. Đây là ràng
   buộc kỹ thuật cứng duy nhất mà phát hiện này sinh ra. Xem §1.7.
3. **Hai họ domain có hai hành vi trái ngược nhau — phải dùng hai cấu hình transport khác nhau:**
   `*.mangadex.org` (API + bìa) chỉ đi được **HTTP/3**; node `*.mangadex.network` (ảnh trang) chỉ đi
   được **TCP/HTTP-2** và **không hề bị chặn**. Xem §1.5.
4. **Không có nguồn thay thế nào cung cấp ảnh trang truyện qua API công khai có tài liệu.** AniList,
   Kitsu, MangaUpdates, Jikan đều chỉ có *metadata*. Comick truy cập được nhưng **không có API công
   khai có tài liệu** → bị loại bởi CONTEXT.md §5. Xem §5. Kết luận này **không đổi** — nhưng vì §1
   đã gỡ bỏ vấn đề chặn, nó không còn là vấn đề sống còn nữa, và việc tách `MangaSource` làm đôi
   (§6.1) trở thành lựa chọn tuỳ ý thay vì bắt buộc.

---

## 1. Phát hiện chặn mạng — kiểm chứng thực nghiệm

Đây là phần quan trọng nhất của tài liệu này và không có trong bất kỳ tài liệu API nào.

> **Đọc §1.3 trở đi trước.** §1.1–§1.2 là đợt đo thứ nhất và kết luận của nó **đã bị bác bỏ**. Giữ lại
> vì quá trình suy luận có ích, nhưng **không dùng để ra quyết định**. Kết luận đang có hiệu lực nằm ở
> §1.4 và §1.5.

### 1.1 Đợt 1 — quy trình kiểm chứng và kết quả (chỉ đo TCP)

**Bước 1 — DNS của router trả IP giả:**

```
$ nslookup api.mangadex.org            # dùng DNS mặc định 192.168.0.1
Addresses:  ::1
            127.0.0.1                  ← IP giả, đây là DNS poisoning
```

**Bước 2 — DNS công cộng trả IP thật:**

```
$ nslookup api.mangadex.org 1.1.1.1
Name:      dxlb.mangadex.org
Addresses: 45.129.229.1
           45.129.229.2               ← IP thật

$ curl "https://cloudflare-dns.com/dns-query?name=api.mangadex.org&type=A"
{"Answer":[{"name":"api.mangadex.org","type":5,"data":"dxlb.mangadex.org."},
           {"name":"dxlb.mangadex.org","type":1,"data":"45.129.229.1"},
           {"name":"dxlb.mangadex.org","type":1,"data":"45.129.229.2"}]}
```

**Bước 3 — kết nối thẳng tới IP thật, bỏ qua DNS hoàn toàn:**

```
$ curl -v --resolve api.mangadex.org:443:45.129.229.1 https://api.mangadex.org/ping
* schannel: disabled automatic use of client certificate
* schannel: failed to receive handshake, SSL/TLS connection failed
exit code 35                          ← TLS handshake bị drop
```

Kết quả giống hệt với `45.129.229.2`.

### 1.2 Kết luận đợt 1 (đã bị thay thế): "chặn SNI"

TLS handshake bị drop **ngay cả khi đã có IP đúng và bỏ qua DNS**. Từ đó bản đầu kết luận: thiết bị
của ISP đọc trường **SNI** trong ClientHello, thấy `mangadex.org` thì drop kết nối.

**Kết luận này không đầy đủ, và phần "hệ quả" của nó thì sai.** Kiểm chứng bổ sung ở §1.3 cho thấy
việc drop **không phụ thuộc vào SNI**, mà phụ thuộc vào **giao thức tầng vận chuyển**. Giữ lại mục
này để ghi nhận quá trình suy luận, không dùng để ra quyết định.

**Điều vẫn đúng:** đổi DNS sang 1.1.1.1 hay dùng DoH **không giải quyết được gì**.

---

### 1.3 Kiểm chứng đợt 2 — bác bỏ giả thuyết SNI

Điểm khởi đầu: mở `https://api.mangadex.org/docs/swagger.html` bằng **Chromium** thì trang **load
bình thường**, trong khi `curl` tới đúng URL đó vẫn fail. Cùng máy, cùng mạng, cùng thời điểm. Khác
biệt này bắt buộc phải giải thích.

**Bước 1 — TCP handshake thực ra KHÔNG bị chặn:**

```
Test-NetConnection 45.129.229.1 -Port 443   ->  TcpTestSucceeded = True
Test-NetConnection 45.129.229.2 -Port 443   ->  TcpTestSucceeded = True
```

**Bước 2 — đổi SNI không thay đổi kết quả.** Nếu là chặn SNI thì SNI mồi phải đi được:

| Thử nghiệm | SNI gửi đi | IP đích | Kết quả |
|---|---|---|---|
| A | `api.mangadex.org` | 45.129.229.1 | exit 35 — handshake bị drop |
| B | `example.com` (mồi) | 45.129.229.1 | exit 35 — y hệt |
| C | không gửi SNI | 45.129.229.1 | exit 35 — y hệt |

Ba kết quả giống hệt nhau ⇒ **SNI không phải là biến quyết định.** Giả thuyết ở §1.2 bị bác bỏ.

**Bước 3 — biến quyết định là giao thức.** DNS HTTPS resource record của MangaDex:

```
$ curl -H "accept: application/dns-json" \
    "https://cloudflare-dns.com/dns-query?name=api.mangadex.org&type=HTTPS"

"data":"1 . alpn=h3,h2 ipv4hint=45.129.229.1,45.129.229.2"
                ^^
```

MangaDex quảng bá **`h3`**. Chrome đọc HTTPS RR này qua Secure DNS rồi đi thẳng bằng QUIC trên UDP
443. `curl` trên máy này (libcurl 8.17 + Schannel) **không được build kèm HTTP/3** — `--http3` báo
"the installed libcurl version does not support this" — nên nó luôn rơi về TCP. Đó là toàn bộ lý do
khiến nó fail.

> HTTPS RR **không** có tham số `ech=`. Vậy ECH (Encrypted Client Hello) **không** liên quan gì ở đây.
> Đây thuần tuý là chuyện TCP so với QUIC.

---

### 1.4 Kiểm chứng quyết định — HTTP/3 so với TCP, cùng một URL

Dùng .NET 10 `HttpClient` với `VersionPolicy = RequestVersionExact` để ép chính xác từng giao thức
(PowerShell 7.6, `[System.Net.Quic.QuicConnection]::IsSupported = True`):

```
GET https://api.mangadex.org/ping
  HTTP/3   (QUIC, UDP 443)  ->  200 OK,  body = "pong"
  HTTP/2   (TCP  443)       ->  FAIL: An existing connection was forcibly closed (RST)
  HTTP/1.1 (TCP  443)       ->  FAIL: An existing connection was forcibly closed (RST)
```

**Kết luận cuối cùng: ISP chặn ở tầng TCP đối với các đích của MangaDex. Lưu lượng QUIC trên UDP 443
không bị đụng tới.** Thiết bị DPI chỉ soi TCP.

**Độ ổn định** — 8 lần thử liên tiếp, mỗi lần mở kết nối mới, không tái sử dụng:

```
HTTP/3 GET /ping  ->  thành công 8/8, thất bại 0
latency: min 51 ms · avg 75 ms · max 208 ms
```

Không phải may mắn một lần. Header trả về xác nhận đây là origin thật của MangaDex, không phải cache
hay trang chặn của ISP:

```
Server:  MangaDex
Via:     HTTP/3.0 AS-EAST-H2x1
Alt-Svc: h3=":443"
X-Request-ID: 01a0497b-7ffd-7867-8a7c-b3ae909b22a4
```

> Response của `GET /manga` **không** chứa header `X-RateLimit-*` nào. Đừng thiết kế rate limiter dựa
> trên việc đọc header đó ở đường thành công — xem §6.2.

---

### 1.5 Bảng khả năng truy cập — đo lại theo từng giao thức

Bảng này thay thế bảng của bản đầu. Bản đầu chỉ đo TCP nên mọi dòng MangaDex đều ra "❌".

| Host | Vai trò | HTTP/3 (QUIC) | HTTP/2 (TCP) | HTTP/1.1 (TCP) |
|---|---|---|---|---|
| `api.mangadex.org` | API chính | ✅ 200 | ❌ RST | ❌ RST |
| `uploads.mangadex.org` | Ảnh bìa | ✅ 200 | ❌ RST | ❌ RST |
| `mangadex.org` | Website | ✅ 200 | ❌ RST | ❌ RST |
| `cmdxd98sb0x3yprd.mangadex.network` | Node MD@H — **ảnh trang** | ❌ QUIC handshake fail | ✅ **200** | ✅ 200 |
| `api.mangadex.network` | Endpoint report MD@H | ❌ QUIC handshake fail | ⚠️ 522 (tới được CF edge) | ⚠️ 522 |

**Hai họ domain hành xử ngược nhau — đây là điểm cần nhớ nhất của cả tài liệu:**

| | `*.mangadex.org` | `*.mangadex.network` |
|---|---|---|
| Hạ tầng | Origin riêng của MangaDex (45.129.229.x) | **Cloudflare** (104.26.2.73, …) |
| HTTPS RR | `alpn=h3,h2` | `alpn=h2` — **không có h3** |
| Bị ISP chặn TCP? | **Có** | **Không** |
| Giao thức dùng được | **Chỉ HTTP/3** | **Chỉ TCP (h2 / h1.1)** |

Nói cách khác: **hai nửa của luồng đọc truyện đi bằng hai giao thức khác nhau, và mỗi nửa không dùng
được giao thức của nửa kia.** Tầng HTTP của Năng lực 5 phải cấu hình được transport theo host.

> `api.mangadex.network` trả **522** — mã lỗi "origin không phản hồi" **do chính Cloudflare sinh ra**,
> nghĩa là ta đã tới được biên Cloudflare. Đây **không phải** ISP chặn. (`/ping` nhiều khả năng đơn
> giản là không tồn tại trên host đó; endpoint thật là `POST /report`.) Cần xác minh lại `POST /report`
> khi hiện thực §3.4 — chưa test vì không muốn gửi report giả.

Các nguồn metadata thay thế đo ở bản đầu (AniList, Kitsu, MangaUpdates, Jikan, GitLab) **vẫn truy cập
được bình thường qua TCP** — không đổi.

---

### 1.6 Kiểm chứng end-to-end — tải ảnh thật về máy

Chạy trọn luồng đọc, mỗi bước dùng đúng giao thức của họ domain tương ứng:

```
1) GET /manga?limit=1&includes[]=cover_art&order[followedCount]=desc     HTTP/3 -> 200  (total = 53 812)
2) GET /chapter?limit=10&translatedLanguage[]=en&contentRating[]=safe    HTTP/3 -> 200
   chapter = 1b23b90e-563f-4e92-b23d-352444eb4a40   pages = 9
3) GET /at-home/server/1b23b90e-563f-4e92-b23d-352444eb4a40              HTTP/3 -> 200
   baseUrl = https://cmdxd98sb0x3yprd.mangadex.network
   hash    = 1f3246c8157f85b243b47366ab5fe33f   data = 9   dataSaver = 9
4) GET {baseUrl}/data/{hash}/1-ee0f9790....png                           HTTP/2 -> 200
   ✅ 1 328 652 byte — ảnh trang thật, tải trọn vẹn
5) GET https://uploads.mangadex.org/covers/{mangaId}/{fileName}          HTTP/3 -> 200
   ✅ 317 676 byte — ảnh bìa
   GET https://uploads.mangadex.org/covers/{mangaId}/{fileName}.512.jpg  HTTP/3 -> 200
   ✅ 124 776 byte — thumbnail 512
```

**Toàn bộ Năng lực 5 chạy được từ mạng gia đình VN, không VPN, không proxy.** Đây là bằng chứng trực
tiếp, không phải suy luận.

Hai ghi chú thực nghiệm quan trọng:

**a) Node bên thứ ba là mặc định, không phải ngoại lệ.** Bước 3 trả về
`cmdxd98sb0x3yprd.mangadex.network` chứ không phải `uploads.mangadex.org` như ví dụ trong tài liệu
chính thức. Nghĩa là **nghĩa vụ report ở §3.4 là có thật và sẽ xảy ra thường xuyên**.

**b) Bẫy chương rỗng — đã gặp thật.** Lần thử đầu chọn trúng một chapter có `pages = 0` (chapter dẫn
ra ngoài). Khi đó `/at-home/server/` vẫn trả **200 OK** nhưng `hash` là chuỗi rỗng và `data` là mảng
rỗng; ghép URL sinh ra `.../data//` vô nghĩa. Khớp đúng với §6.3: **phải** kiểm tra `pages > 0` và
`externalUrl == null` **trước** khi gọi at-home. Đây là bẫy đã gặp, không phải giả định.

---

### 1.7 Hệ quả bắt buộc lên tầng client Android

Đây là phần **duy nhất** của phát hiện này tạo ra ràng buộc kỹ thuật mới — và nó mâu thuẫn với stack
đã chốt ở CONTEXT.md §3.

CONTEXT.md §3 chốt **Ktor client**. Trên Android, Ktor chạy trên engine OkHttp / CIO / Android —
**không engine nào trong số đó nói được HTTP/3**. Với OkHttp, `Protocol.HTTP_3` chỉ tồn tại như một
hằng số enum để một interceptor bên ngoài dùng; issue QUIC của OkHttp
([square/okhttp#907](https://github.com/square/okhttp/issues/907), mở từ 2014) vẫn nằm trong Icebox.

⇒ **Với engine mặc định, app Android sẽ KHÔNG gọi được `api.mangadex.org` từ mạng VN**, kể cả khi
browser trên cùng chiếc máy đó làm được.

Các lựa chọn, theo thứ tự khuyến nghị:

| Phương án | HTTP/3? | Chi phí APK | Đánh giá |
|---|---|---|---|
| **Cronet qua Play Services**<br>`com.google.android.gms:play-services-cronet` | ✅ | ~30 KB | Ngăn xếp mạng của Chromium. Nhẹ, Google tự cập nhật vá bảo mật. **Khuyến nghị.** Cần Play Services trên máy. |
| **Cronet nhúng** (`cronet-embedded` → `cronet-bundled`) | ✅ | ~10 MB | Không phụ thuộc Play Services. Cảnh báo: artifact trên Maven đã cũ và đang đổi tên — **phải kiểm tra lại toạ độ + tình trạng bảo trì trước khi chốt.** |
| Giữ Ktor + OkHttp thuần | ❌ | 0 | **Không dùng được từ VN.** |
| `org.chromium.net:cronet-fallback` | ❌ | nhỏ | `JavaCronetProvider` chỉ HTTP/1.1. **Không cứu được gì** — đừng nhầm với Cronet thật. |

Cách ghép Cronet vào code:

- **`google/cronet-transport-for-okhttp`** — do Google phát hành, cắm Cronet làm transport cho OkHttp,
  nên `HttpClient(OkHttp)` của Ktor dùng lại được mà gần như không đổi call site. Đường ít rủi ro nhất.
- `niusounds/cronet-engine` — engine Ktor dùng thẳng Cronet. Dự án cộng đồng, giai đoạn sớm; đọc kỹ
  trước khi phụ thuộc.
- Gọi thẳng Cronet API trong `/core/manga-network` và **không** dùng Ktor cho riêng Năng lực 5.

Phương án cuối khớp rất đẹp với quy tắc cô lập của CONTEXT.md §5 ("**không** dùng chung Ktor client
với tailnet — client riêng, cấu hình riêng, `User-Agent` riêng"): tầng tailnet giữ nguyên Ktor+OkHttp,
Năng lực 5 dùng Cronet. Ranh giới cô lập đã có sẵn khiến ngoại lệ này không lan ra phần còn lại của
dự án, và việc "xoá hai thư mục là gỡ sạch Năng lực 5" vẫn đúng.

**Đây là quyết định thêm dependency ⇒ theo CONTEXT.md §9 phải hỏi trước, không tự làm.** Xem §7.

---

### 1.8 Rủi ro và độ bền của phát hiện này

Nói thẳng để sau này không bị bất ngờ:

1. **QUIC có thể bị chặn bất cứ lúc nào.** Hiện ISP chỉ soi TCP. Chặn UDP 443 là việc dễ làm nếu họ
   muốn. Kiến trúc **phải** coi "không tới được MangaDex" là một trạng thái bình thường có xử lý tử
   tế, không phải crash. Đây là lý do §6.1 (tách nguồn catalog / content) vẫn đáng cân nhắc.
2. **Kết quả này đo trên đúng một mạng, một ISP, một ngày.** Mạng di động (4G/5G) **chưa đo** và có
   thể khác — nhiều nhà mạng di động bóp hoặc chặn UDP 443 mạnh hơn mạng cố định.
   **Việc phải làm trước khi viết code: đo lại trên 4G bằng chính chiếc điện thoại sẽ chạy app.**
3. **Không có fallback ở tầng ứng dụng.** Vì `*.mangadex.org` chỉ đi được h3, nếu QUIC hỏng thì không
   còn đường nào trong app. Fallback duy nhất là VPN ở tầng hệ điều hành — nằm ngoài phạm vi app.
4. **Cronet là dependency nặng ký hơn OkHttp** về bề mặt bảo trì, kể cả bản Play Services.

---

## 2. MangaDex API — tham chiếu đã xác minh

Base URL: `https://api.mangadex.org` · Phiên bản API: 5.x · **Không cần xác thực** cho toàn bộ phạm
vi đọc mà Năng lực 5 cần.

### 2.1 Yêu cầu bắt buộc của MangaDex

Trích nguyên văn từ `2-limitations.md`:

- Request **MUST** có header `User-Agent`, **và không được giả mạo** (must not be spoofed).
- Request **CANNOT** có header `Via` (không cho phép proxy không trong suốt).
- Bắt buộc TLS. Hỗ trợ HTTP/1.1, HTTP/2, HTTP/3 (QUIC). Yêu cầu client hỗ trợ SNI, TLS 1.2 (đang bị
  deprecated) hoặc TLS 1.3.
- MangaDex **cố tình trả ảnh sai** (`https://uploads.mangadex.org/agg.jpg`) cho mọi ảnh bị hotlink từ
  domain khác.

Áp dụng cho dự án — phù hợp với CONTEXT.md §5:

```kotlin
// /core/manga-network — Ktor client RIÊNG, không dùng chung với tailnet
private const val USER_AGENT = "PersonalDeviceHub/0.1 (Android; personal sideload build)"
```

`User-Agent` phải định danh thật ứng dụng. **Không** copy chuỗi UA của trình duyệt — vừa vi phạm
"must not be spoofed", vừa vi phạm quy tắc §5 "không giả mạo request của trình duyệt để lách".

### 2.2 Rate limit — con số chính thức

**Giới hạn toàn cục:** ~**5 request/giây trên mỗi địa chỉ IP** cho toàn bộ `api.mangadex.org`.

Tài liệu nói rõ đây là *mức tối thiểu được đảm bảo*, không phải giá trị chính xác — thực tế có thể
cao hơn do enforce ở tầng load balancer. **Không được thiết kế dựa vào phần dư này.**

**Giới hạn theo endpoint** (chỉ liệt kê endpoint mà Năng lực 5 dùng — phần còn lại là ghi/upload):

| Endpoint | Số request | Chu kỳ (phút) |
|---|---|---|
| `GET /at-home/server/{id}` | **40** | 1 |
| `GET /manga/random` | 60 | 1 |
| `POST /auth/login` | 30 | 60 |
| `POST /auth/refresh` | 60 | 60 |

Các endpoint đọc khác (`/manga`, `/manga/{id}/feed`, `/chapter`, `/cover`) **không có** giới hạn
riêng — chỉ chịu giới hạn toàn cục 5 req/s.

**Header trả về khi gọi endpoint có giới hạn riêng:**

| Header | Ý nghĩa |
|---|---|
| `X-RateLimit-Limit` | Số request tối đa của endpoint trong chu kỳ |
| `X-RateLimit-Remaining` | Số request còn lại trong chu kỳ hiện tại |
| `X-RateLimit-Retry-After` | **UNIX timestamp** thời điểm kết thúc chu kỳ (không phải số giây!) |

> ⚠️ `X-RateLimit-Retry-After` là **timestamp tuyệt đối**, khác với header `Retry-After` chuẩn HTTP
> (thường là số giây). Tính nhầm ở đây sẽ dẫn tới backoff sai hoàn toàn.

**Hậu quả leo thang khi vượt giới hạn** — đọc kỹ, đây là lý do phải làm backoff tử tế:

1. Vượt giới hạn → HTTP **429** cho mọi request tới `*.mangadex.org`.
2. Vẫn tiếp tục gửi khi đang bị 429 → DDoS protection kích hoạt → **ban IP tạm thời**, trả HTTP **403**
   cho mọi request. Thời lượng không công bố.
3. Vẫn tiếp tục gửi → MangaDex **ngừng trả lời hoàn toàn**, và thời gian cooldown **được gia hạn lại
   với mỗi request bạn gửi thêm** trong lúc đang bị chặn.

Điểm 3 rất quan trọng: **retry mù khi bị chặn sẽ tự kéo dài thời gian bị chặn**. Bắt buộc phải có
circuit breaker, không chỉ backoff.

Tài liệu còn cảnh báo riêng về "abusive patterns": nếu gửi 500 request cho việc mà 1 request làm được,
sẽ bị throttle nặng hoặc chặn hẳn.

### 2.3 Giới hạn phân trang

- `offset + limit > 10.000` → request **bị từ chối**.
- `limit` tối đa **100** cho hầu hết endpoint (một số endpoint feed cho tới 500).
- `offset` lớn hơn tổng số phần tử → trả về collection rỗng.

Response phân trang trả kèm `limit`, `offset`, `total`.

### 2.4 Reference Expansion — kỹ thuật giảm số request quan trọng nhất

Thay vì gọi `/manga` rồi gọi tiếp `/cover/{id}` cho từng truyện (N+1 request → chính là "abusive
pattern" mà MangaDex cảnh báo), dùng `includes[]`:

```http
GET /manga?limit=20&includes[]=cover_art&includes[]=author&includes[]=artist
```

Thuộc tính của quan hệ được nhúng thẳng vào mảng `relationships`. Với danh sách 20 truyện, việc này
giảm từ 21+ request xuống **1 request**.

Lưu ý từ tài liệu: nếu một quan hệ không expand được (thiếu quyền), request **vẫn thành công và không
có dấu hiệu lỗi nào** — nên code phải xử lý trường hợp `attributes` của relationship là null.

Giá trị `includes[]` dùng cho Năng lực 5: `cover_art`, `author`, `artist`, `scanlation_group`, `manga`.

### 2.5 Các endpoint cần cho Năng lực 5

| Mục đích | Endpoint | Ghi chú |
|---|---|---|
| Tìm kiếm / duyệt danh sách | `GET /manga` | limit mặc định 10, tối đa 100 |
| Chi tiết truyện | `GET /manga/{id}` | Dùng `includes[]` |
| Danh sách chương | `GET /manga/{id}/feed` | limit mặc định 100 |
| Tóm tắt volume/chương | `GET /manga/{id}/aggregate` | Nhẹ hơn feed, tốt cho mục lục |
| Chi tiết chương | `GET /chapter/{id}` | |
| **Ảnh trang chương** | `GET /at-home/server/{chapterId}` | Luồng 2 bước — xem §3 |
| Danh sách tag | `GET /manga/tag` | Cache lại, tag hiếm khi đổi |
| Truyện ngẫu nhiên | `GET /manga/random` | 60/phút |

### 2.6 Tham số tìm kiếm `GET /manga` — danh sách đầy đủ

Lấy từ OpenAPI spec chính thức:

| Tham số | Kiểu | Mặc định | Ghi chú |
|---|---|---|---|
| `limit` | int | 10 | Tối đa 100 |
| `offset` | int | 0 | `offset + limit ≤ 10000` |
| `title` | string | — | Tìm theo tựa |
| `authorOrArtist` | string | — | |
| `authors[]`, `artists[]` | UUID[] | — | |
| `year` | int | — | Năm phát hành |
| `includedTags[]` | UUID[] | — | Dùng UUID tag, không dùng tên |
| `includedTagsMode` | enum | `AND` | `AND` \| `OR` |
| `excludedTags[]` | UUID[] | — | |
| `excludedTagsMode` | enum | `OR` | `AND` \| `OR` |
| `status[]` | enum[] | — | `ongoing`, `completed`, `hiatus`, `cancelled` |
| `originalLanguage[]` | string[] | — | |
| `excludedOriginalLanguage[]` | string[] | — | |
| `availableTranslatedLanguage[]` | string[] | — | Lọc theo ngôn ngữ có bản dịch |
| `publicationDemographic[]` | enum[] | — | `shounen`, `shoujo`, `josei`, `seinen` |
| `ids[]` | UUID[] | — | Tối đa 100/request |
| `contentRating[]` | enum[] | `["safe","suggestive","erotica"]` | Xem cảnh báo bên dưới |
| `createdAtSince`, `updatedAtSince` | timestamp | — | |
| `order[...]` | object | `latestUploadedChapter: desc` | Xem §2.7 |
| `includes[]` | string[] | — | Reference expansion |
| `hasAvailableChapters` | string | — | |
| `group` | UUID | — | Lọc theo nhóm dịch |

> ⚠️ **`contentRating` mặc định đã bao gồm `erotica`.** Chỉ `pornographic` là bị ẩn mặc định. Nếu muốn
> chỉ nội dung an toàn, phải truyền tường minh `contentRating[]=safe`.

**Cú pháp tham số mảng và object trong Ktor:**

```kotlin
// Mảng: lặp lại key với hậu tố []
url { parameters.append("includes[]", "cover_art") }   // → includes[]=cover_art

// Object (order): cú pháp ngoặc vuông
url { parameters.append("order[relevance]", "desc") }  // → order[relevance]=desc
```

### 2.7 Tuỳ chọn sắp xếp

**Manga:** `title`, `year`, `createdAt`, `updatedAt`, `latestUploadedChapter` (mặc định `desc`),
`followedCount`, `relevance` — mỗi cái nhận `asc` | `desc`.

**Chapter:** `createdAt`, `updatedAt`, `publishAt`, `readableAt`, `volume`, `chapter` — mặc định `asc`.

Gợi ý dùng: tìm kiếm theo tên → `order[relevance]=desc`; trang chủ "phổ biến" → `order[followedCount]=desc`;
"mới cập nhật" → `order[latestUploadedChapter]=desc`; mục lục chương → `order[chapter]=asc`.

### 2.8 Ngôn ngữ và localization

API dùng mã ISO 639-1 hai chữ, có mở rộng 5 ký tự khi cần:

| Mã | Nghĩa |
|---|---|
| `zh` | Trung giản thể |
| `zh-hk` | Trung phồn thể |
| `pt-br` | Bồ Đào Nha (Brazil) |
| `es` | Tây Ban Nha (Castilian) |
| `es-la` | Tây Ban Nha (Mỹ Latin) |
| `ja-ro` / `ko-ro` / `zh-ro` | Bản romaji hoá |

Tiếng Việt là `vi`. Các trường `title`, `description`, `altTitles` là **map ngôn ngữ → chuỗi**, không
phải chuỗi đơn:

```kotlin
@Serializable
data class MangaAttributes(
    val title: Map<String, String>,              // {"en": "...", "ja": "..."}
    val altTitles: List<Map<String, String>> = emptyList(),
    val description: Map<String, String> = emptyMap(),
    // ...
)

/** Chọn tựa theo thứ tự ưu tiên, có fallback — map có thể không chứa khoá mong muốn. */
fun Map<String, String>.pick(vararg prefer: String): String? =
    prefer.firstNotNullOfOrNull { this[it] } ?: values.firstOrNull()
```

Không được giả định khoá `"en"` luôn tồn tại.

### 2.9 Schema các thực thể chính

Lấy từ OpenAPI spec chính thức. Mọi response bọc trong envelope:

```json
{ "result": "ok", "response": "collection", "data": [...], "limit": 10, "offset": 0, "total": 123 }
```

**MangaAttributes:** `title`, `altTitles`, `description`, `isLocked`, `links`, `originalLanguage`,
`lastVolume`, `lastChapter`, `publicationDemographic`, `status`, `year`, `contentRating`,
`chapterNumbersResetOnNewVolume`, `availableTranslatedLanguages`, `latestUploadedChapter`, `tags`,
`state`, `version`, `createdAt`, `updatedAt`.

**ChapterAttributes:** `title`, `volume`, `chapter`, `pages` (số ảnh đọc được), `translatedLanguage`,
`uploader`, `externalUrl`, `version`, `createdAt`, `updatedAt`, `publishAt`, `readableAt`.

> ⚠️ **`externalUrl` khác null nghĩa là chương này host ở nơi khác** (ví dụ trang chính thức của nhà
> xuất bản). Những chương đó **không có ảnh qua at-home** — gọi `/at-home/server/{id}` sẽ thất bại.
> Trình đọc phải kiểm tra trường này trước và hiển thị "chương ở nguồn ngoài" thay vì crash.
> Feed có tham số `includeExternalUrl` để lọc.

**CoverAttributes:** `volume`, `fileName`, `description`, `locale`, `version`, `createdAt`, `updatedAt`.

**Quan hệ:** mảng `relationships`, mỗi phần tử có `id`, `type`, và `attributes` (chỉ có khi dùng
`includes[]`), một số có thêm `related`.

### 2.10 Ảnh bìa

Không có endpoint tải bìa. URL được **ghép tay** từ manga id + `fileName` của cover:

```
https://uploads.mangadex.org/covers/{manga-id}/{cover-filename}
https://uploads.mangadex.org/covers/{manga-id}/{cover-filename}.256.jpg   ← thumbnail 256px
https://uploads.mangadex.org/covers/{manga-id}/{cover-filename}.512.jpg   ← thumbnail 512px
```

Lưu ý: thumbnail giữ **nguyên đuôi file gốc** rồi mới nối thêm. Với `abc.png` thì thumbnail là
`abc.png.256.jpg`, **không phải** `abc.256.jpg`.

Lấy `fileName` qua reference expansion (`includes[]=cover_art`), không gọi thêm `/cover/{id}`.

Dùng bản 256 cho lưới danh sách, 512 cho trang chi tiết. **Không tải bản gốc** cho danh sách — lãng
phí băng thông của MangaDex và của người dùng.

---

## 3. Luồng lấy ảnh chương (MangaDex@Home) — phần khó nhất

CONTEXT.md §5 nói đúng: đây là luồng hai bước, không phải URL tĩnh.

> **Ghi chú transport (từ §1.5) — áp dụng cho cả mục 3 này.** Hai bước của luồng chạy trên hai giao
> thức khác nhau khi truy cập từ VN:
>
> | Bước | Host | Giao thức bắt buộc |
> |---|---|---|
> | 1. `GET /at-home/server/{id}` | `api.mangadex.org` | **HTTP/3 (QUIC)** — TCP bị RST |
> | 2. `GET {baseUrl}/{quality}/{hash}/{file}` | thường là `*.mangadex.network` | **TCP (h2/h1.1)** — node không có h3 |
> | 3. `POST /report` | `api.mangadex.network` | **TCP (h2/h1.1)** |
>
> Nếu `baseUrl` trả về lại là `uploads.mangadex.org` (MangaDex tự phục vụ) thì bước 2 phải đi
> **HTTP/3**. ⇒ **Client ảnh phải chọn giao thức theo host của `baseUrl` tại runtime, không hardcode.**
> Cronet tự thương lượng được việc này qua Alt-Svc/HTTPS RR; đây là một lý do nữa để dùng Cronet (§1.7).

### 3.1 Bước 1 — lấy metadata phân phối ảnh

```http
GET https://api.mangadex.org/at-home/server/{chapterId}?forcePort443=false
```

| Tham số | Kiểu | Mặc định | Ghi chú |
|---|---|---|---|
| `chapterId` | UUID (path) | — | |
| `forcePort443` | bool (query) | `false` | Xem §3.5 |

Response:

```json
{
  "result": "ok",
  "baseUrl": "https://uploads.mangadex.org",
  "chapter": {
    "hash": "3303dd03ac8d27452cce3f2a882e94b2",
    "data":      ["1-f7a76de1....png", "2-2a5e95df....png"],
    "dataSaver": ["1-27e74764....jpg", "2-b4e2cd69....jpg"]
  }
}
```

| Trường | Kiểu | Mô tả |
|---|---|---|
| `baseUrl` | string | Base URL để ghép URL ảnh |
| `chapter.hash` | string | Hash của chương |
| `chapter.data` | string[] | Tên file chất lượng gốc, **đã theo đúng thứ tự trang** |
| `chapter.dataSaver` | string[] | Tên file bản nén, cùng thứ tự |

### 3.2 Bước 2 — ghép URL ảnh

```
{baseUrl}/{quality}/{hash}/{filename}
```

`quality` là `data` (gốc) hoặc `data-saver` (nén). Ví dụ:

```
https://uploads.mangadex.org/data/3303dd03ac8d27452cce3f2a882e94b2/1-f7a76de1....png
https://uploads.mangadex.org/data-saver/3303dd03ac8d27452cce3f2a882e94b2/1-27e74764....jpg
```

### 3.3 Ba cái bẫy — tài liệu cảnh báo tường minh

**1. `baseUrl` chỉ là một chuỗi. Không được parse, không được giả định định dạng.**

Trích nguyên văn: *"Do NOT assume any format. It is not 'a URL', it is not 'a domain name', it's not
'https:// followed by a domain name'. It is a string. No more no less. Just use it as-is."*

Node MangaDex@Home có thể trả về dạng như `https://foo.bar:5678/abcdef/1a2b3c4d` — có cổng lạ **và**
có path prefix. Chỉ nối chuỗi.

**2. `baseUrl` hết hạn sau ~15 phút.** Tài liệu đảm bảo *tối thiểu* 15 phút, "có thể hơn, có thể kém".
Hết hạn → HTTP **403**. Phải gọi lại `/at-home/server/{chapterId}` để lấy base URL mới.

Đây là ràng buộc thiết kế thật: người dùng đọc chậm một chương dài sẽ vượt 15 phút giữa chừng. Trình
đọc **phải** xử lý 403 bằng cách refresh base URL rồi thử lại, không phải báo lỗi.

**3. TUYỆT ĐỐI không gửi header xác thực khi tải ảnh.** Tài liệu:

- Gửi auth header tới image server → request **bị từ chối**.
- Nếu là node bên thứ ba trên `mangadex.network` → **bạn đang rò rỉ token cho người vận hành node đó**.

Với dự án này thì không có auth (guest), nhưng nếu Ktor client cấu hình một `Auth` plugin dùng chung
thì nó sẽ tự gắn header vào mọi request. Đây chính là một lý do nữa để giữ client ảnh **tách riêng**
— khớp với quy tắc cô lập của CONTEXT.md §5.

### 3.4 Endpoint báo cáo MangaDex@Home — bắt buộc về mặt đạo đức

CONTEXT.md §5 hỏi endpoint report "có còn hiệu lực không" — **có, vẫn còn và vẫn được yêu cầu.**

Với **mỗi ảnh** tải từ base URL **không chứa** `mangadex.org` (tức là node tình nguyện bên thứ ba),
dù thành công hay thất bại:

```http
POST https://api.mangadex.network/report
Content-Type: application/json
```

| Trường | Kiểu | Mô tả |
|---|---|---|
| `url` | string | URL **đầy đủ** của ảnh, gồm cả `https://` |
| `success` | bool | Tải thành công hay không |
| `cached` | bool | `true` khi và chỉ khi header `X-Cache` bắt đầu bằng `HIT` |
| `bytes` | number | Kích thước ảnh (byte). Lỗi không có response → `0` |
| `duration` | number | Thời gian tải **trọn vẹn** tính bằng ms (không phải TTFB) |

Hai lưu ý từ tài liệu: `Content-Type` phải **chính xác** là `application/json`; domain là
`api.mangadex.network`, **không phải** `api.mangadex.org`.

> **Đo thực tế 2026-08-29:** `api.mangadex.network` nằm sau **Cloudflare** và **không bị ISP chặn**
> (khác hẳn kết luận của bản đầu). `GET /ping` trả **522** — lỗi origin do Cloudflare sinh ra, tức là
> ta tới được biên CF; nhiều khả năng `/ping` đơn giản không tồn tại. Host này **chỉ quảng bá `alpn=h2`,
> không có h3** ⇒ report phải gửi qua **TCP**, không phải QUIC. `POST /report` **chưa được test** vì
> không muốn gửi report giả — xác minh khi hiện thực.
>
> Và lần đo end-to-end (§1.6) trả về node bên thứ ba `cmdxd98sb0x3yprd.mangadex.network`, nên nghĩa vụ
> report này **sẽ kích hoạt thường xuyên**, không phải trường hợp hiếm.

Nếu tải ảnh thất bại: gọi report **và** gọi lại `/at-home/server/{chapterId}` để lấy base URL mới.
Tài liệu nói thẳng — nếu bạn không report, MangaDex không thể biết node đó hỏng và sẽ tiếp tục gán
node hỏng đó cho bạn.

> Với ảnh tải từ `uploads.mangadex.org` (chính MangaDex) thì **không cần** report.

### 3.5 `forcePort443`

Node MangaDex@Home không bắt buộc chạy trên cổng 443. Một số mạng trường học/công ty chặn cổng lạ.
Đặt `forcePort443=true` sẽ chỉ chọn node dùng cổng 443 chuẩn.

Với dự án này: nên để **cấu hình được** trong Settings, mặc định `false`. Nếu người dùng gặp lỗi tải
ảnh trên mạng hạn chế thì bật lên.

### 3.6 Không hardcode base URL

Tài liệu cảnh báo bằng giọng mỉa mai nhưng nội dung nghiêm túc: URL động được tối ưu theo vị trí địa
lý; hardcode sẽ chậm hơn, tốn băng thông của MangaDex, và các URL cố định có rate limit chặt hơn.

---

## 4. Xác thực — không cần cho Năng lực 5

CONTEXT.md §5 đã quyết định: không đăng nhập tài khoản. Ghi lại ở đây để không ai đi nhầm đường.

MangaDex dùng **OAuth 2** qua `auth.mangadex.org` (Keycloak, realm `mangadex`, token endpoint
`/realms/mangadex/protocol/openid-connect/token`). Hai loại client:

- **Public client** (`authorization_code` flow) — *chưa khả dụng* tính đến bản tài liệu hiện tại.
- **Personal client** (`password` flow) — chỉ dùng được với chính tài khoản sở hữu client. Access
  token sống 15 phút, làm mới bằng refresh token. Đăng ký tại mangadex.org/settings.

Chỉ cần khi: theo dõi truyện trên tài khoản MD, đồng bộ tiến độ đọc, danh sách cá nhân, đánh dấu đã
đọc. **Toàn bộ đều nằm ngoài phạm vi Năng lực 5** — tiến độ đọc lưu trong Room, cục bộ.

Hai lời khuyên hiệu năng từ tài liệu, đáng ghi nhớ nếu sau này có đổi ý:

1. Đừng gửi auth header khi không cần — **request có xác thực không được cache**.
2. Đừng bao giờ gửi auth header tới domain nào ngoài `{api, auth}.mangadex.org`.

---

## 5. Các nguồn manga công khai khác — khảo sát

Tiêu chí đánh giá theo CONTEXT.md §5: *"Chỉ dùng API công khai đã có tài liệu. Không scrape HTML,
không đọc ngược API nội bộ không công bố. Nếu một trang không có API công khai, ta không hỗ trợ trang
đó."*

### 5.1 Bảng so sánh

| Nguồn | Giao thức | Có tài liệu? | Metadata | **Ảnh trang truyện** | Vào được từ VN | Kết luận |
|---|---|---|---|---|---|---|
| **MangaDex** | REST | ✅ Đầy đủ | ✅ | ✅ | ❌ **Bị chặn** | Nguồn duy nhất có nội dung đọc |
| **AniList** | GraphQL | ✅ | ✅ Tốt | ❌ | ✅ | Chỉ metadata |
| **Kitsu** | REST (JSON:API) | ✅ | ✅ | ❌ | ✅ | Chỉ metadata |
| **MangaUpdates** | REST | ✅ | ✅ Rất tốt | ❌ | ✅ | Chỉ metadata |
| **Jikan** (MAL) | REST | ✅ | ✅ | ❌ | ⚠️ Upstream lỗi | Chỉ metadata |
| **Comick** | REST | ❌ Không | ✅ | ✅ | ✅ | **Loại** — xem §5.4 |

### 5.2 Kết luận then chốt

**Không nguồn thay thế nào cung cấp ảnh trang truyện qua API công khai có tài liệu.** AniList, Kitsu,
MangaUpdates, Jikan là các dịch vụ *danh mục và theo dõi* — chúng biết truyện tồn tại, có bao nhiêu
chương, ai vẽ, bìa ra sao, nhưng **không host nội dung**.

Đã kiểm chứng: truy vấn AniList cho One Piece trả về `chapters: null`, `volumes: null`, đầy đủ staff
và `coverImage` — đúng như mong đợi của một dịch vụ metadata.

Nghĩa là: **kiến trúc `MangaSource` không thể "đổi nguồn" để né chặn MangaDex.** Đây là điều CONTEXT.md
§12 câu hỏi 6 đang bỏ ngỏ, và câu trả lời có ảnh hưởng lớn hơn dự kiến.

### 5.3 Chi tiết kỹ thuật các nguồn metadata

**AniList** — `https://graphql.anilist.co`, GraphQL, không cần key cho truy vấn công khai.

Rate limit đọc từ response header thực tế: `X-RateLimit-Limit: 30` (mỗi phút). Có phơi
`X-RateLimit-Remaining` và `X-RateLimit-Reset` qua CORS.

Truy vấn đã kiểm chứng chạy được:

```graphql
{ Media(search: "One Piece", type: MANGA) {
    id title { romaji english native } chapters volumes
    coverImage { large } siteUrl
} }
```

**Kitsu** — `https://kitsu.io/api/edge/`, chuẩn JSON:API. Cần header `Accept: application/vnd.api+json`.
Không cần key. Không phơi header rate limit. Tổng kho: ~62.993 manga (đọc từ `meta.count`).

```http
GET https://kitsu.io/api/edge/manga?filter[text]=one%20piece&page[limit]=1
```

**MangaUpdates** — `https://api.mangaupdates.com/v1/`. Tìm kiếm dùng **POST**, không phải GET:

```http
POST https://api.mangaupdates.com/v1/series/search
Content-Type: application/json
{"search": "one piece", "perpage": 1}
```

Metadata rất tốt cho manga (nhóm dịch, quan hệ series, thể loại) — mảng mạnh nhất trong các nguồn
metadata cho đúng lĩnh vực manga.

**Jikan** — `https://api.jikan.moe/v4/`, proxy không chính thức của MyAnimeList, không cần key. Lúc
test trả `504 BadResponseException` (MyAnimeList upstream không phản hồi) — đây là điểm yếu cố hữu
của kiến trúc proxy: nó chỉ khoẻ bằng upstream.

### 5.4 Vì sao loại Comick

`api.comick.cc` truy cập được từ Việt Nam và có nội dung đọc. Nhưng khi thăm dò:

```
GET https://api.comick.cc/openapi.json   → HTTP 200 nhưng trả HTML, không phải JSON
GET https://api.comick.cc/docs           → HTTP 200 nhưng trả HTML "This domain is for s..."
```

Không có OpenAPI spec, không có tài liệu công khai. Các thư viện dùng Comick đều dựa trên **đọc ngược
API nội bộ**. CONTEXT.md §5 cấm rõ điều này. **Loại.**

Ghi lại ở đây để lần sau không phải điều tra lại — và để khi ai đó đề xuất "dùng Comick đi", có sẵn
câu trả lời.

---

## 6. Áp dụng vào kiến trúc — đề xuất cụ thể

### 6.0 Tầng transport — ràng buộc cứng, quyết định trước mọi thứ khác

Rút ra từ §1.5 và §1.7. Đây là mục **bắt buộc**, khác với §6.1 vốn là tuỳ chọn.

1. **Client của Năng lực 5 phải nói được HTTP/3.** Không có nó thì `api.mangadex.org` không gọi được
   từ VN. Ktor+OkHttp mặc định **không đủ**. Xem bảng phương án ở §1.7.
2. **Chọn giao thức theo host tại runtime, không hardcode.** `baseUrl` từ `/at-home/server/` có thể là
   `*.mangadex.network` (chỉ TCP) hoặc `uploads.mangadex.org` (chỉ HTTP/3). Cronet tự thương lượng
   được qua Alt-Svc và HTTPS RR — đây là lý do kỹ thuật chính để chọn nó, ngoài chuyện h3.
3. **Ranh giới cô lập của CONTEXT.md §5 giúp ích chứ không cản trở.** Vì §5 đã cấm dùng chung client
   với tailnet, việc Năng lực 5 chạy Cronet còn tầng tailnet chạy Ktor+OkHttp **không phá vỡ gì cả**.
   Không cần đổi §3 của CONTEXT.md cho phần tailnet.
4. **Coi "không tới được MangaDex" là trạng thái bình thường của UI**, không phải lỗi bất thường.
   ISP có thể chặn UDP 443 bất cứ lúc nào (§1.8). Cần một trạng thái lỗi tử tế, có nút thử lại.
5. **Đo lại trên 4G trước khi viết code.** Toàn bộ số liệu ở §1 đo trên Wi-Fi mạng cố định.

### 6.1 Tách `MangaSource` thành hai vai trò

Phát hiện ở §5.2 nói rằng một interface `MangaSource` duy nhất là mô hình sai: các nguồn không thay
thế được cho nhau, chúng làm hai việc khác nhau.

```kotlin
// /core/manga-network

/** Nguồn danh mục: tìm kiếm, metadata, bìa. AniList/Kitsu/MangaUpdates/MangaDex đều làm được. */
interface MangaCatalogSource {
    val id: SourceId
    suspend fun search(query: SearchQuery): Result<Paged<MangaSummary>>
    suspend fun detail(mangaId: MangaRef): Result<MangaDetail>
}

/** Nguồn nội dung: danh sách chương + ảnh trang. Hiện chỉ MangaDex làm được. */
interface MangaContentSource {
    val id: SourceId
    suspend fun chapters(mangaId: MangaRef, languages: List<String>): Result<Paged<ChapterSummary>>
    suspend fun pages(chapterId: ChapterRef, quality: ImageQuality): Result<ChapterPages>
}

/** MangaDex hiện thực cả hai. */
class MangaDexSource(...) : MangaCatalogSource, MangaContentSource
```

Lợi ích: khi MangaDex không tới được, UI vẫn có thể duyệt/tìm kiếm qua AniList và hiển thị rõ ràng
"không có nguồn đọc khả dụng" — thay vì màn hình trắng.

> **Đã hạ mức ưu tiên sau phát hiện §1.** Bản đầu coi việc tách này gần như bắt buộc vì tưởng MangaDex
> bị chặn hoàn toàn. Giờ MangaDex dùng được, nên đây quay về đúng bản chất: một khoản bảo hiểm rẻ tiền
> cho rủi ro §1.8 mục 1, không phải giải pháp cho một vấn đề đang xảy ra.

Chi phí: gần như bằng không nếu làm ngay, đúng như lập luận trong CONTEXT.md §5 về việc trừu tượng
hoá sớm.

> Nếu bạn quyết định **chỉ dùng MangaDex và không bao giờ thêm nguồn khác** (một lựa chọn hợp lý cho
> app cá nhân), thì đừng tách — giữ một interface duy nhất. CONTEXT.md §9 nói "đừng thiết kế quá mức".
> Việc tách chỉ đáng làm nếu câu trả lời cho §12 câu hỏi 6 là "có".

### 6.2 Rate limiter và backoff — yêu cầu tối thiểu

Dựa trên §2.2, tầng HTTP bắt buộc phải có:

1. **Token bucket toàn cục** giới hạn ~**3 req/s** (đặt dưới ngưỡng 5 để có biên an toàn — nhớ rằng
   giới hạn tính theo IP và có thể chia sẻ với thiết bị khác trong nhà).
2. **Bucket riêng cho `/at-home/server/`**: 40/phút.
3. **Xử lý 429**: đọc `X-RateLimit-Retry-After` như **UNIX timestamp**, chờ tới thời điểm đó. Không
   dùng backoff cố định. *Đo thực tế: response **thành công** không hề kèm header `X-RateLimit-*`
   (§1.4) — nên rate limiter phải tự đếm phía client, chỉ đọc header ở đường 429.*
4. **Circuit breaker cho 403**: khi gặp 403 (dấu hiệu bị ban IP tạm thời), **ngừng gửi hoàn toàn**
   trong một khoảng dài và tăng dần. Đây không phải tối ưu — theo tài liệu, retry lúc này sẽ **gia hạn
   thời gian bị chặn**.
5. **Không tải song song ồ ạt.** CONTEXT.md §5 đã nói. Prefetch tối đa 2–3 ảnh phía trước.

### 6.3 Trình đọc ảnh — checklist rút ra từ tài liệu

- Kiểm tra `chapter.attributes.externalUrl != null` **trước** khi gọi at-home (§2.9).
- Lưu `baseUrl` kèm **thời điểm lấy**; coi như hết hạn sau ~15 phút; xử lý **403 = refresh, không phải
  lỗi** (§3.3).
- Xử lý `baseUrl` như chuỗi thuần, không parse (§3.3).
- Đo `bytes` và `duration` của từng ảnh; đọc header `X-Cache`; POST report khi base URL không chứa
  `mangadex.org` (§3.4).
- Client tải ảnh **không có** auth header (§3.3).
- Mặc định `data-saver` cho mạng di động, `data` cho Wi-Fi — cấu hình được.
- Giới hạn số bitmap giữ trong bộ nhớ; cache đĩa có trần dung lượng (CONTEXT.md §5).

### 6.4 Về thư viện tải ảnh (CONTEXT.md §12 câu hỏi 5)

Yêu cầu của luồng at-home vượt xa "tải một URL": base URL hết hạn cần refresh + retry, cần đo
bytes/duration cho report, cần header `X-Cache`, cần client không có auth.

Coil hỗ trợ được qua `OkHttpClient`/`Interceptor` tuỳ biến, nhưng phần **refresh base URL** phải nằm ở
tầng repository phía trên chứ không nhét vừa vào image loader. Nghĩa là: Coil lo decode/cache/bộ nhớ
(phần mà tự viết chắc chắn sẽ sai), repository lo vòng đời base URL và report.

**Bổ sung sau §1.7:** Coil cho phép cắm `OkHttpClient` riêng, và
`google/cronet-transport-for-okhttp` cho phép `OkHttpClient` đó chạy trên Cronet. Nghĩa là **Coil +
Cronet ghép được**, và cùng một client dùng chung cho ảnh bìa (`uploads.mangadex.org`, cần h3) lẫn ảnh
trang (`*.mangadex.network`, TCP). Nếu không có đường ghép này thì ảnh bìa sẽ không tải được từ VN —
đây là điểm phải kiểm chứng bằng một spike nhỏ **trước** khi chốt Coil.

Vẫn là quyết định của bạn theo §9 — nhưng dữ liệu để quyết đã đủ.

---

## 7. Việc cần bạn quyết định

Theo CONTEXT.md §9 ("không đoán, hãy hỏi") và §12:

1. **HTTP client nào cho Năng lực 5?** — *câu hỏi chặn đường, thay thế câu hỏi "xử lý chặn thế nào"
   của bản đầu.* Ktor+OkHttp mặc định không gọi được MangaDex từ VN (§1.7). Đề xuất:
   **`play-services-cronet` (~30 KB) ghép vào OkHttp qua `cronet-transport-for-okhttp`**, giữ nguyên
   Ktor ở tầng tailnet. Đây là **thêm dependency** ⇒ cần bạn đồng ý theo CONTEXT.md §9.
2. **Đo lại trên 4G.** Trước khi viết code, chạy lại §1.4 trên mạng di động của chính điện thoại sẽ
   cài app. Nếu 4G chặn UDP 443 thì kết luận §1 chỉ đúng ở nhà, và bài toán quay lại như bản đầu.
3. **Có thêm nguồn ngoài MangaDex không** (CONTEXT.md §12 câu hỏi 6)? Quyết định có tách
   `MangaSource` làm đôi (§6.1) hay không. Đã hạ từ "gần như bắt buộc" xuống "tuỳ chọn" — xem §6.1.
4. **Coil hay tự viết** (CONTEXT.md §12 câu hỏi 5)? Dữ liệu ở §6.4. Ràng buộc mới: image loader phải
   chạy được trên transport có h3.

Việc đã làm: phát hiện §1 đã được ghi vào CONTEXT.md §5 (thay thế mục "Chưa xác minh"), §11 (nhật ký
quyết định) và §12 (câu hỏi mở).

---

## 8. Nguồn

Tài liệu chính thức MangaDex (repo GitLab là source của `api.mangadex.org/docs`):

- [Limitations and Requirements](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/2-limitations.md) — rate limit, yêu cầu User-Agent
- [Retrieving a chapter's images](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/04-chapter/retrieving-chapter.md) — luồng at-home, endpoint report
- [Retrieving Covers](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/03-manga/covers.md)
- [Reference Expansion](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/01-concepts/reference-expansion.md)
- [Pagination](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/01-concepts/pagination.md)
- [Searching for a manga](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/03-manga/search.md)
- [Static Data / Enumerations](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/3-enumerations.md)
- [Authentication](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/02-authentication/index.md)
- [OpenAPI spec gốc](https://gitlab.com/mangadex-pub/mangadex-api-docs/-/blob/main/static/api.yaml)

Schema và tham số endpoint đối chiếu từ client sinh tự động từ spec chính thức
([openapi-mangadex-python](https://github.com/mahmudindev/openapi-mangadex-python), API v5.10.2).

Các nguồn khác: [AniList GraphQL](https://graphql.anilist.co) · [Kitsu API](https://kitsu.io/api/edge/) ·
[MangaUpdates API](https://api.mangaupdates.com/v1/) · [Jikan](https://api.jikan.moe/v4/)

Về HTTP/3 và client Android (§1.7):

- [OkHttp — HTTP/3 support, issue #907](https://github.com/square/okhttp/issues/907) — mở từ 2014, vẫn ở Icebox
- [Perform network operations using Cronet — Android Developers](https://developer.android.com/develop/connectivity/cronet) — Cronet hỗ trợ HTTP/3 over QUIC
- [Send a simple request — Cronet](https://developer.android.com/develop/connectivity/cronet/start) — toạ độ `com.google.android.gms:play-services-cronet`
- [google/cronet-transport-for-okhttp](https://github.com/google/cronet-transport-for-okhttp) — cắm Cronet làm transport cho OkHttp/Retrofit
- [niusounds/cronet-engine](https://github.com/niusounds/cronet-engine) — engine Ktor dùng Cronet (cộng đồng, giai đoạn sớm)
- [Maven: org.chromium.net:cronet-embedded](https://mvnrepository.com/artifact/org.chromium.net/cronet-embedded) — bản nhúng ~10 MB, artifact đang chuyển đổi

Toàn bộ kết quả đo khả năng truy cập ở §1 là kiểm chứng thực nghiệm từ máy PC của dự án, ngày
2026-08-29 (đợt 1: `curl`/`nslookup`; đợt 2: `Test-NetConnection`, Chromium, và .NET 10 `HttpClient`
với `VersionPolicy = RequestVersionExact` để ép từng giao thức).
