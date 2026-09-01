# Điều tra API phim — kết quả đo thật, 2026-08-31

> Đọc file này trước khi viết dòng code đầu tiên của Năng lực 7.
> Luật nằm ở [CONTEXT.md §5b](../CONTEXT.md).

Mọi con số dưới đây **đo bằng HTTP thật** từ mạng gia đình, không suy luận, không lấy từ trí nhớ.

---

## 1. Kết luận ngắn

| Nguồn | Vai trò | Dùng được? |
|---|---|---|
| **Internet Archive** | Metadata **và** stream video | ✅ **Có** — nguồn chính |
| **TMDB** | Chỉ metadata (poster, mô tả, diễn viên) | ⚠️ Chưa đo được (xem §4) |
| **OMDb** | Chỉ metadata | ⚠️ Cần API key, còn 1000 request/ngày |
| Các API kiểu `vidsrc`, `consumet`, `fmovies`, `superembed` | Stream phim thương mại | ❌ **Không** — xem §5 |

**Chốt: chỉ dùng Internet Archive.** Đây là nguồn duy nhất vừa hợp pháp, vừa cho stream thật, vừa
không cần khoá.

---

## 2. Internet Archive — đã kiểm chứng đầy đủ

### 2.1. Ba endpoint cần dùng

Không cần API key. Không cần đăng ký.

```
# Tìm kiếm + phân trang
GET https://archive.org/advancedsearch.php
    ?q=collection:"feature_films" AND licenseurl:[* TO *]
    &fl[]=identifier&fl[]=title&fl[]=year&fl[]=licenseurl
    &rows=20&page=1&output=json

# Chi tiết một mục + danh sách file
GET https://archive.org/metadata/{identifier}

# Tải/stream file
GET https://archive.org/download/{identifier}/{tên file}
```

### 2.2. Số liệu đo được

| Phép đo | Kết quả |
|---|---|
| `advancedsearch.php` | **200**, 0.92 s |
| `metadata/{id}` | **200**, 0.58 s |
| Tổng số mục trong `feature_films` | **28.482** |
| Số mục **có khai báo license** | **9.050** |
| Tỷ lệ có license (mẫu 200) | **37%** |
| Tải file video | **206 Partial Content** |
| `Accept-Ranges` | `bytes` — **tua được** |
| File thật đã tải thử | `Night Alarm.mp4`, 203.137.386 byte (194 MB) |

### 2.3. Ràng buộc kiến trúc: `download` chuyển hướng sang node vùng

```
GET https://archive.org/download/night-alarm/Night%20Alarm.mp4
→ 302 Found
→ Location: https://dn710200.ca.archive.org/0/items/night-alarm/Night%20Alarm.mp4
```

Giống hệt luồng MangaDex@Home ở §5: **URL cuối là một node vùng, không cố định**.

Hệ quả bắt buộc:

- **Phải theo redirect** (`-L` / `AllowAutoRedirect`). Không giả định host đích.
- **Không cache URL đã resolve** — node có thể đổi. Cache `identifier` + tên file, resolve lại mỗi
  phiên xem.
- **Không hardcode `dn*.archive.org`** vào bất kỳ đâu, kể cả cấu hình.

### 2.4. Range request — nền tảng của việc tua

Đã kiểm chứng `Range: bytes=0-102399` → **206**, nhận đúng 102.400 byte.

Đây là thứ cho phép trình phát tua tới giữa phim mà không tải cả 194 MB. Backend proxy **phải
chuyển tiếp nguyên vẹn** header `Range` của trình duyệt và trả lại `206` + `Content-Range`. Nuốt
mất `Range` là mất khả năng tua, và người dùng phải chờ tải hết file.

### 2.5. Rate limit

Internet Archive **không công bố con số cụ thể**. Điều đã biết chắc:

- Trả **429 Too Many Requests** khi vượt ngưỡng.
- Có thể kèm header **`Retry-After`** — phải tôn trọng, không thử lại ngay.
- IA nói rõ họ **thay đổi ngưỡng bất cứ lúc nào**.
- Điều khoản cấm scrape ồ ạt không kiểm soát tốc độ.

⇒ Bắt buộc có backoff, và **không** tải song song nhiều luồng để nhanh hơn.

### 2.6. Bẫy đã gặp thật

- **Đoán identifier là hỏng.** Thử `night_of_the_living_dead` → API trả 200 nhưng
  `metadata` **rỗng**, không có `title`, không có file nào. Luôn lấy identifier từ kết quả tìm kiếm,
  đừng tự ghép chuỗi.
- **`scrape` API có `count` tối thiểu là 100.** Gửi `count=3` → `RangeException`. Dùng
  `advancedsearch.php` cho phân trang nhỏ.
- **Tên file có dấu cách** (`Night Alarm.mp4`) → phải URL-encode. Không encode là 404.

---

## 3. Giấy phép — điểm quan trọng nhất của cả năng lực này

**Không phải mọi thứ trong `feature_films` đều là public domain.** Đo được: chỉ **37%** khai báo
license (9.050/28.482). Số còn lại **không khai báo gì cả** — nghĩa là không xác minh được, chứ
không phải "mặc định tự do".

Các license gặp trong mẫu 200:

| Số lần | License |
|---|---|
| 25 + 17 | `creativecommons.org/publicdomain/mark/1.0/` (http và https) |
| 12 | `creativecommons.org/licenses/by-nc-nd/4.0/` |
| 7 | `creativecommons.org/licenses/publicdomain/` |
| 5 | `creativecommons.org/publicdomain/zero/1.0/` |

**Tin tốt: lọc được ngay ở tầng truy vấn.** Đã kiểm chứng cú pháp này chạy đúng:

```
q=collection:"feature_films" AND licenseurl:[* TO *]
```

⇒ Biến câu hỏi pháp lý thành một bộ lọc kỹ thuật. Đây là lý do năng lực này **khả thi** trong khi
"API phim free" nói chung thì không.

Xem CONTEXT.md §5b để biết ràng buộc bắt buộc quanh chuyện này.

---

## 4. TMDB — chưa kết luận được

| Phép đo | Kết quả |
|---|---|
| `www.themoviedb.org` | **200** |
| `api.themoviedb.org` | **000** (không kết nối được) |
| DNS `api.themoviedb.org` | resolver sandbox không trả lời |

**Không được kết luận "TMDB bị chặn ở VN" từ dữ liệu này.** Lý do đúng bằng bài học của
`manga-api-research.md`: `curl` trên máy này **không hỗ trợ HTTP/3**
(`--http3-only: the installed libcurl version does not support this`), và MangaDex đã từng bị kết
luận nhầm là "chặn hoàn toàn" đúng vì công cụ đo thiếu năng lực.

Phải đo lại bằng trình duyệt thật hoặc `HttpClient` của .NET (có HTTP/3 sẵn) trước khi kết luận.

**Điều khoản TMDB** (đọc từ tài liệu chính thức):

- Miễn phí cho **mục đích phi thương mại**, bắt buộc **ghi nguồn**.
- Thương mại cần thoả thuận riêng bằng văn bản.
- **Cấm dùng cho ứng dụng ML/AI.**
- Chỉ có **metadata** — TMDB không cung cấp link phim.

Dự án này là cá nhân, phi thương mại ⇒ hợp điều kiện miễn phí. Nhưng nó là **tuỳ chọn làm đẹp**,
không phải thứ bắt buộc.

---

## 5. Vì sao KHÔNG dùng các "API phim free" phổ biến

Các API kiểu `vidsrc`, `consumet`, `superembed`, `fmovies`, `2embed` xuất hiện đầu bảng khi tìm
"free movie API". **Không dùng bất kỳ cái nào.** Lý do kỹ thuật, không phải lời răn đạo đức:

1. **Chúng phục vụ nội dung không có giấy phép.** Đây là phim thương mại bị scrape lậu. §5 của
   CONTEXT.md chấp nhận MangaDex vì nó có API công khai, có tài liệu, có rate limit chính thức.
   Nhóm này không có gì trong số đó.
2. **Đổi tên miền liên tục** vì bị gỡ. Một dependency đổi địa chỉ vài tháng một lần là nợ kỹ thuật
   vĩnh viễn — hôm nay chạy, tháng sau hỏng, không ai bảo trì.
3. **Không có hợp đồng API.** Không tài liệu chính thức, không cam kết tương thích ngược, không rate
   limit công bố. Đổi cấu trúc JSON bất cứ lúc nào.
4. **Thường kèm mã theo dõi và quảng cáo pop-up** trong iframe nhúng — mà §6 của CONTEXT.md cấm đưa
   thứ không kiểm soát được vào hệ thống có quyền tắt máy từ xa.
5. **Rủi ro pháp lý thật.** §1 cho phép dùng cá nhân, nhưng hệ thống này **đã mở ra Internet qua
   Cloudflare Tunnel** (§4a) — khác hẳn một app sideload chỉ mình dùng.

Nếu sau này muốn xem phim thương mại: dùng đúng ứng dụng của nhà cung cấp. Đừng nhét vào hub này.

---

## 6. Việc phải làm trước khi viết code

- [ ] **Đo lại TMDB** bằng `HttpClient` của .NET (có HTTP/3) — xem có thật sự vào được không.
- [ ] Đo tốc độ tải từ node vùng ở giờ cao điểm. Lần đo này được ~51 KB/s trên đoạn 100 KB đầu, quá
      chậm để phát trực tiếp nếu duy trì — nhưng mẫu quá nhỏ để kết luận. **Phải đo lại với đoạn
      lớn hơn.**
- [ ] Kiểm tra định dạng video thực tế trong tập dữ liệu: có bao nhiêu mục là MP4/h.264 (trình duyệt
      phát được) so với MPEG2/MKV (không phát được nếu không chuyển mã).

---

## 7. Nguồn

- [Internet Archive Developer Portal](https://archive.org/developers/ias3.html)
- [TMDB API Terms of Use](https://www.themoviedb.org/api-terms-of-use)
- [TMDB API for Business](https://www.themoviedb.org/api-for-business)
- [Internet Archive rate limit — thảo luận công khai](https://github.com/internetarchive/wayback/issues/274)
