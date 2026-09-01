<#
.SYNOPSIS
    Chép theme Device Hub sang thư mục cài MeshCentral.

.DESCRIPTION
    Theme sống trong repo (có lịch sử Git, khôi phục được, cài lại máy khác được)
    nhưng MeshCentral đọc từ thư mục cài của nó. Script này nối hai chỗ đó.

    Đích là `meshcentral-web/public/styles/` — thư mục override CHÍNH THỨC của
    MeshCentral. Nó được ưu tiên hơn `node_modules`, nên `npm update` không xoá
    mất theme. Không sửa gì bên trong node_modules.

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
$target = Join-Path $MeshCentralPath 'meshcentral-web\public'

if (-not (Test-Path $MeshCentralPath)) {
    throw "Không tìm thấy MeshCentral ở '$MeshCentralPath'. Dùng -MeshCentralPath để chỉ chỗ khác."
}

if ($Remove) {
    $css = Join-Path $target 'styles\custom.css'
    if (Test-Path $css) {
        Remove-Item -Force $css
        Write-Host "Đã gỡ theme. Khởi động lại MeshCentral để thấy diện mạo gốc." -ForegroundColor Yellow
    }
    else {
        Write-Host "Không có theme nào để gỡ." -ForegroundColor Yellow
    }
    return
}

# Chép từng file thay vì cả thư mục: meshcentral-web có thể chứa file override
# khác của người dùng, không được đạp lên.
$files = Get-ChildItem -Recurse $source -File
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\')
    $destination = Join-Path $target $relative
    $destinationDir = Split-Path $destination -Parent

    New-Item -ItemType Directory -Force $destinationDir | Out-Null
    Copy-Item $file.FullName $destination -Force
    Write-Host "  $relative" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Đã chép $($files.Count) file sang $target" -ForegroundColor Green
Write-Host ""
Write-Host "Bước tiếp theo — MeshCentral chỉ đọc file web lúc khởi động:" -ForegroundColor Cyan
Write-Host "  Restart-Service MeshCentral      # nếu chạy như service"
Write-Host ""
Write-Host "Rồi tải lại trang bằng Ctrl+Shift+R (bỏ qua cache trình duyệt)." -ForegroundColor Cyan
