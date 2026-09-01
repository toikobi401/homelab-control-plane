/*
 * Theme Device Hub cho MeshCentral — phần JavaScript
 * ==================================================
 *
 * TÊN FILE PHẢI LÀ `custom.js`: MeshCentral nạp sẵn `scripts/custom.js` trong
 * mọi trang (đo được bằng trình duyệt thật — nó nằm trong danh sách 43 script).
 * Đặt tên khác, ví dụ `theme.js`, thì file không bao giờ chạy.
 *
 * CSS lo gần như toàn bộ diện mạo. File này CHỈ làm những việc CSS không với
 * tới được, và cố ý giữ nhỏ: mỗi dòng JS chạm vào một ứng dụng ta không viết
 * là một dòng có thể phá chức năng của nó.
 *
 * NGUYÊN TẮC:
 *   - Không sửa hành vi. Không gắn/gỡ sự kiện của MeshCentral.
 *   - Không giả định phần tử tồn tại — MeshCentral dựng DOM động.
 *   - Hỏng thì phải im lặng, không được làm chết trang.
 */

(function () {
  'use strict';

  /*
   * 1. Nạp font Inter
   * -----------------
   * Hub dùng Inter. CSS `font-family` không tự tải font — phải có @font-face
   * hoặc link tới nguồn. Nhúng bằng JS thay vì @import trong CSS vì @import
   * chặn render, còn thẻ <link> thì không.
   *
   * Không tải được (mất mạng, CSP chặn) thì rơi về system-ui — đã khai sẵn
   * trong chuỗi fallback của CSS, nên không cần xử lý lỗi.
   */
  function loadFont() {
    if (document.getElementById('hub-theme-font')) return;

    var link = document.createElement('link');
    link.id = 'hub-theme-font';
    link.rel = 'stylesheet';
    link.href =
      'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap';
    // display=swap: hiện chữ bằng font dự phòng ngay, đổi sang Inter khi tải
    // xong. Không có nó thì chữ vô hình trong lúc chờ.
    document.head.appendChild(link);
  }

  /*
   * 2. Khai báo với MeshCentral rằng theme này nền tối
   * -------------------------------------------------
   * MeshCentral gọi `MeshCentralTheme.isDarkBaseTheme(...)` để quyết định có
   * bật `data-bs-theme="dark"` hay không (xem default3.handlebars). Đây là
   * điểm cắm CHÍNH THỨC — dùng nó thay vì tự set attribute, để không đá nhau
   * với logic night mode sẵn có.
   *
   * Chỉ bọc thêm, không thay thế: giữ nguyên mọi theme Bootswatch người dùng
   * có thể đã chọn.
   */
  function registerDarkTheme() {
    if (typeof window.MeshCentralTheme === 'undefined') return;

    var original = window.MeshCentralTheme.isDarkBaseTheme;
    if (typeof original !== 'function') return;
    if (original.__hubWrapped) return; // Không bọc hai lần khi script nạp lại.

    var wrapped = function (theme) {
      // Theme của hub luôn tối. Các theme khác vẫn hỏi hàm gốc.
      var normalized =
        typeof window.MeshCentralTheme.normalizeTheme === 'function'
          ? window.MeshCentralTheme.normalizeTheme(theme)
          : theme;

      if (normalized === 'default') return true;
      return original.call(window.MeshCentralTheme, theme);
    };

    wrapped.__hubWrapped = true;
    window.MeshCentralTheme.isDarkBaseTheme = wrapped;
  }

  /*
   * 3. Đánh dấu trang để CSS nhắm được
   * ----------------------------------
   * Trang đăng nhập và trang chính dùng template khác nhau nhưng không có class
   * phân biệt. Thêm một class để CSS viết quy tắc riêng cho từng trang mà không
   * phải dựa vào việc phần tử nào tồn tại.
   */
  function markPage() {
    var body = document.body;
    if (!body) return;

    // `#centralTable` chỉ có ở trang đăng nhập; `#masthead` chỉ có sau khi vào.
    if (document.getElementById('centralTable')) {
      body.classList.add('hub-login');
    } else if (document.getElementById('masthead')) {
      body.classList.add('hub-app');
    }
  }

  function init() {
    try {
      loadFont();
      registerDarkTheme();
      markPage();
    } catch (err) {
      // Theme hỏng không được kéo theo cả ứng dụng. Ghi log để còn lần ra,
      // rồi để MeshCentral chạy tiếp với diện mạo gốc.
      if (window.console && console.warn) {
        console.warn('[hub-theme] bỏ qua lỗi khi khởi tạo:', err);
      }
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
