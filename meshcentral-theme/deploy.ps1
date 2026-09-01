<#
.SYNOPSIS
    Chép theme Device Hub sang thư mục cài MeshCentral.

.DESCRIPTION
    Theme sống trong repo (có lịch sử Git, khôi phục được, cài lại máy khác được)
    nhưng MeshCentral đọc từ thư mục cài của nó. Script này nối hai chỗ đó.

    Đích là `node_modules/meshcentral/public/` — nơi MeshCentral thật sự phục vụ
    file tĩnh. `custom.css` và `custom.js` ở đó vốn RỖNG và được nạp sẵn trong
    mọi trang: chúng sinh ra để người dùng ghi đè.

    VÌ SAO KHÔNG DÙNG `meshcentral-web/`: thư mục override đó KHÔNG hoạt động
    trên cài đặt này. Đã kiểm chứng bằng thực nghiệm — đặt một file thử vào
    `meshcentral-web/public/styles/` rồi gọi qua HTTP thì server trả 404, dù
    đường dẫn, quyền đọc và thứ tự middleware đều đúng. Nghi do service chạy từ
    `WinService\daemon` nên `__dirname` mà MeshCentral dùng để dò override lệch
    khỏi chỗ ta đặt file.

    ĐÁNH ĐỔI: `npm update meshcentral` sẽ ghi đè hai file này. Chạy lại script
    sau mỗi lần cập nhật. Script tự lưu bản gốc (.orig) lần đầu để `-Remove`
    trả về đúng nguyên trạng.

.PARAMETER MeshCentralPath
    Thư mục cài MeshCentral. Mặc định D:\App\MeshCentral.

.PARAMETER Remove
    Gỡ theme, trả MeshCentral về diện mạo gốc.

.EXAMPLE
    .\deploy.ps1
    .\deploy.ps1 -Remove
#>
[CmdletBinding()]
param(
    [string]$MeshCentralPath = 'D:\App\MeshCentral',
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'public'
$target = Join-Path $MeshCentralPath 'node_modules\meshcentral\public'

if (-not (Test-Path $MeshCentralPath)) {
    throw "Không tìm thấy MeshCentral ở '$MeshCentralPath'. Dùng -MeshCentralPath để chỉ chỗ khác."
}

if (-not (Test-Path $target)) {
    throw "Không thấy '$target'. MeshCentral đã cài đúng chưa?"
}

$files = Get-ChildItem -Recurse $source -File

if ($Remove) {
    $restored = 0
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        $destination = Join-Path $target $relative
        $backup = "$destination.orig"

        # Trả lại đúng nội dung gốc, không phải xoá trắng: MeshCentral trông đợi
        # hai file này TỒN TẠI (nó nạp chúng vô điều kiện). Xoá đi sẽ thành 404.
        if (Test-Path $backup) {
            Copy-Item $backup $destination -Force
            Remove-Item -Force $backup
            $restored++
        }
    }
    Write-Host "Đã khôi phục $restored file gốc. Khởi động lại MeshCentral." -ForegroundColor Yellow
    return
}

foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\')
    $destination = Join-Path $target $relative
    $backup = "$destination.orig"

    # Lưu bản gốc MỘT lần duy nhất. Chạy script lần hai không được đè bản sao
    # lưu trữ bằng chính theme của ta — làm thế là mất đường lui.
    if ((Test-Path $destination) -and -not (Test-Path $backup)) {
        Copy-Item $destination $backup
    }

    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    Copy-Item $file.FullName $destination -Force
    Write-Host "  $relative" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Đã chép $($files.Count) file sang $target" -ForegroundColor Green
Write-Host ""
Write-Host "KHÔNG cần khởi động lại MeshCentral — file tĩnh đọc theo từng request." -ForegroundColor Cyan
Write-Host ""
Write-Host "NHƯNG PHẢI xoá cache trình duyệt:" -ForegroundColor Yellow
Write-Host "  MeshCentral gửi 'Cache-Control: max-age=14400' (4 giờ) cho custom.css."
Write-Host "  Trình duyệt sẽ dùng bản cũ đúng 4 tiếng nếu không ép nạp lại."
Write-Host ""
Write-Host "  Ctrl+Shift+R        — nạp lại bỏ qua cache" -ForegroundColor Cyan
Write-Host "  hoặc mở DevTools > Network > tick 'Disable cache' rồi F5"
Write-Host ""
Write-Host "Không làm bước này thì trông như theme không có tác dụng." -ForegroundColor Yellow
