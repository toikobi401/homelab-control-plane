namespace Hub.Core.Backup;

/// <summary>
/// Mẫu nội dung file filter, để giao diện đưa ra như gợi ý sẵn.
///
/// Cú pháp là của **rclone** (<c>--filter-from</c>), gần `.gitignore` nhưng
/// không giống hệt: mỗi dòng bắt đầu bằng <c>-</c> (loại trừ) hoặc <c>+</c>
/// (giữ lại), <c>**</c> khớp nhiều cấp thư mục, <c>#</c> là chú thích.
///
/// Dòng <c>+ **</c> ở cuối là **bắt buộc** với mọi mẫu: rclone áp luật theo thứ
/// tự từ trên xuống và dừng ở dòng khớp đầu tiên, nên thiếu nó thì những file
/// không khớp luật nào sẽ bị bỏ qua thay vì được sao lưu.
/// </summary>
public static class FilterPresets
{
    public static IReadOnlyList<FilterPreset> All { get; } =
    [
        new("Dự án code",
            "Bỏ thư mục build và thư viện tải về — thứ tạo lại được từ mã nguồn.",
            """
            # Thư viện và thư mục build — tạo lại được, không cần sao lưu
            - node_modules/**
            - bin/**
            - obj/**
            - target/**
            - dist/**
            - build/**
            - .venv/**
            - __pycache__/**
            - .git/**

            # Còn lại lấy hết
            + **
            """),

        new("File tạm",
            "Bỏ file tạm của hệ điều hành và trình soạn thảo.",
            """
            # File tạm — không có giá trị khi khôi phục
            - *.tmp
            - *.temp
            - *.swp
            - *~
            - Thumbs.db
            - desktop.ini
            - .DS_Store

            # Còn lại lấy hết
            + **
            """),

        new("Chỉ ảnh",
            "Chỉ sao lưu ảnh, bỏ mọi thứ khác.",
            """
            # Chỉ giữ ảnh
            + *.jpg
            + *.jpeg
            + *.png
            + *.heic
            + *.gif
            + *.webp
            + *.raw
            + *.dng

            # Mọi thứ khác bỏ qua
            - **
            """),

        new("Bỏ file nặng",
            "Bỏ video và file nén lớn — thứ chiếm phần lớn dung lượng cloud.",
            """
            # File nặng, cân nhắc sao lưu riêng
            - *.iso
            - *.vhd
            - *.vhdx
            - *.mp4
            - *.mkv
            - *.avi
            - *.zip
            - *.7z

            # Còn lại lấy hết
            + **
            """)
    ];
}

/// <param name="Name">Tên hiển thị trên giao diện.</param>
/// <param name="Description">Một câu nói rõ mẫu này loại trừ gì.</param>
/// <param name="Content">Nội dung file, ghi nguyên văn — .NET không parse.</param>
public sealed record FilterPreset(string Name, string Description, string Content);
