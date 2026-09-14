import 'react'

/**
 * `webkitdirectory` cho `<input type="file">`.
 *
 * Đây là cách DUY NHẤT để trình duyệt mở hộp thoại chọn **thư mục** của hệ điều
 * hành và trả về toàn bộ nội dung bên trong. Dùng cho chế độ tải thư mục lên
 * (§5d): máy chủ không đọc được đĩa của máy khác, nên thư mục của máy đang mở
 * web phải do chính trình duyệt đọc rồi đẩy lên hub.
 *
 * Vì sao phải khai tay: thuộc tính này có tiền tố nhà cung cấp nên không nằm
 * trong kiểu `InputHTMLAttributes` chuẩn của React, dù mọi trình duyệt cần dùng
 * đều hỗ trợ (Chrome, Edge, Firefox, Safari, Chrome trên Android).
 *
 * Không dùng `showDirectoryPicker` thay thế: nó chỉ có trên Chromium, chết trên
 * Firefox và toàn bộ Safari — mà mỗi máy trong tailnet sẽ tự mở web UI của nó,
 * nên phải chạy được trên mọi trình duyệt.
 */
declare module 'react' {
  // Tham số T phải khớp đúng số lượng với khai báo gốc của React, dù phần mở
  // rộng này không dùng tới — nên tham chiếu nó một lần cho lint khỏi báo thừa.
  interface InputHTMLAttributes<T = HTMLInputElement> {
    /** Không dùng; chỉ để giữ tham số T khớp khai báo gốc. */
    readonly __webkitdirectoryElement?: T
    /** Chọn cả thư mục thay vì từng tệp. Mỗi `File` có thêm `webkitRelativePath`. */
    webkitdirectory?: string
    /** Firefox dùng tên không tiền tố cho cùng hành vi. */
    directory?: string
  }
}
