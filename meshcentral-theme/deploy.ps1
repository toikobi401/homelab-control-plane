<#
.SYNOPSIS
    Chép theme Device Hub sang thư mục cài MeshCentral.

.DESCRIPTION
    Theme sống trong repo (có lịch sử Git, khôi phục được, cài lại máy khác được)
    nhưng MeshCentral đọc từ thư mục cài của nó. Script này nối hai chỗ đó.

    Chép vào CẢ HAI đích, vì mỗi chỗ có vai trò riêng:

      1. `meshcentral-web/public/`  — thư mục override chính thức. MeshCentral
         ƯU TIÊN chỗ này, và `npm update` KHÔNG đụng tới.
      2. `node_modules/meshcentral/public/` — nơi phục vụ mặc định. Là dự phòng
         nếu override vì lý do nào đó không được nhận.

    LỊCH SỬ MỘT KẾT LUẬN SAI: có lúc script này chỉ chép vào `node_modules` vì
    một phép thử cho thấy file mới trong `meshcentral-web` trả 404. Kết luận đó
    SAI — override vẫn hoạt động, chỉ là MeshCentral chốt danh sách file lúc
    khởi động nên file thêm sau không được nhận cho tới lần restart kế tiếp.
    Bằng chứng: sau restart, ETag server trả về là `2f35` = 12085 byte, khớp
    đúng kích thước file trong `meshcentral-web`, không phải bản trong
    `node_modules`.

    Hệ quả thực tế: chép một chỗ mà quên chỗ kia thì override cũ sẽ che mất bản
    mới — đúng cái bẫy đã làm mất một lượt sửa.

    ĐÁNH ĐỔI: `npm update meshcentral` ghi đè bản trong `node_modules` (bản
    trong `meshcentral-web` thì không). Chạy lại script sau mỗi lần cập nhật.
    Script tự lưu bản gốc (.orig) lần đầu để `-Remove` trả về đúng nguyên trạng.

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

if (-not (Test-Path $MeshCentralPath)) {
    throw "Không tìm thấy MeshCentral ở '$MeshCentralPath'. Dùng -MeshCentralPath để chỉ chỗ khác."
}

$nodeModules = Join-Path $MeshCentralPath 'node_modules\meshcentral\public'
if (-not (Test-Path $nodeModules)) {
    throw "Không thấy '$nodeModules'. MeshCentral đã cài đúng chưa?"
}

# Cả hai đích. `meshcentral-web` được MeshCentral ưu tiên, nên phải đứng trước —
# bỏ sót nó là để bản cũ ở đó che mất bản mới.
$targets = @(
    (Join-Path $MeshCentralPath 'meshcentral-web\public'),
    $nodeModules
)

$files = Get-ChildItem -Recurse $source -File

if ($Remove) {
    $restored = 0
    foreach ($target in $targets) {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($source.Length).TrimStart('\')
            $destination = Join-Path $target $relative
            $backup = "$destination.orig"

            if (Test-Path $backup) {
                # Có bản gốc: trả lại đúng nội dung đó. MeshCentral trông đợi
                # hai file này TỒN TẠI (nó nạp chúng vô điều kiện) nên xoá trắng
                # sẽ thành 404.
                Copy-Item $backup $destination -Force
                Remove-Item -Force $backup
                $restored++
            }
            elseif ((Test-Path $destination) -and $target -ne $nodeModules) {
                # Trong `meshcentral-web` thì không có bản gốc để trả — thư mục
                # này hoàn toàn do ta tạo. Xoá là đúng: MeshCentral tự rơi về
                # bản trong node_modules.
                Remove-Item -Force $destination
                $restored++
            }
        }
    }
    Write-Host "Đã gỡ theme khỏi $restored file. Khởi động lại MeshCentral." -ForegroundColor Yellow
    return
}

foreach ($target in $targets) {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        $destination = Join-Path $target $relative
        $backup = "$destination.orig"

        # Lưu bản gốc MỘT lần duy nhất, và chỉ trong node_modules (chỗ có file
        # gốc của MeshCentral). Chạy script lần hai không được đè bản sao lưu
        # trữ bằng chính theme của ta — làm thế là mất đường lui.
        if ($target -eq $nodeModules -and (Test-Path $destination) -and -not (Test-Path $backup)) {
            Copy-Item $destination $backup
        }

        New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
        Copy-Item $file.FullName $destination -Force
    }

    Write-Host "  $($files.Count) file -> $target" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Đã chép vào cả hai đích." -ForegroundColor Green
Write-Host ""
Write-Host "Còn HAI bước nữa, thiếu bước nào cũng thành 'theme không có tác dụng':" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Khởi động lại MeshCentral (PowerShell quyền admin):" -ForegroundColor Cyan
Write-Host "     Restart-Service 'meshcentral.exe'"
Write-Host ""
Write-Host "     MeshCentral chốt danh sách file web lúc khởi động. File THÊM MỚI"
Write-Host "     không được nhận cho tới lần restart kế tiếp; file GHI ĐÈ lên chỗ"
Write-Host "     đã có thì đọc lại được ngay."
Write-Host ""
Write-Host "  2. Xoá cache trình duyệt: Ctrl+Shift+R" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Kiểm chứng — so kích thước server trả về với file trong repo:" -ForegroundColor DarkGray
Write-Host "     (Invoke-WebRequest 'https://<mesh>/styles/custom.css?v=1' -UseBasicParsing).Content.Length"
Write-Host ""
Write-Host "     Lệch nhiều nghĩa là đang phục vụ bản cũ ở đích còn lại." -ForegroundColor DarkGray
