<#
.SYNOPSIS
    Vá MeshCentral để lệnh cài agent dùng tên miền công khai thay vì tên tailnet.

.DESCRIPTION
    Giao diện MeshCentral sinh lệnh cài agent (Linux, Windows, mobile) từ
    `serverinfo.name`, mà giá trị đó lấy từ CommonName của chứng chỉ — tức tên
    tailnet. Tên đó **chỉ phân giải được từ máy đã cài Tailscale**.

    Hệ quả trên máy ngoài tailnet:

        wget "https://<tên>.ts.net:4430/meshagents?script=1" ...
        Resolving <tên>.ts.net... failed: Name or service not known
        bash: ./meshinstall.sh: Permission denied

    Dòng "Permission denied" gây hiểu nhầm — nó chỉ là hệ quả: wget hỏng nên
    meshinstall.sh chưa bao giờ được tải về.

    Khoá `agentaliasdns` trong config.json ĐÃ khai tên công khai, nhưng
    MeshCentral chỉ dùng nó cho `magenturl` (app di động), không cho lệnh cài.
    Bản vá này cho `serverinfo.name` ưu tiên `agentaliasdns`.

    Vì sao sửa serverinfo.name mà không đặt domain.dns:
    `domain.dns` ảnh hưởng 28 chỗ trong webserver.js/meshuser.js/meshcentral.js
    và có nguy cơ làm hỏng đường tailnet đang chạy tốt. `serverinfo.name` chỉ
    dùng ở 8 chỗ trong default.handlebars, tất cả đều là lệnh cài agent hoặc URL
    để agent kết nối vào — đúng thứ cần địa chỉ công khai.

    ⚠️ Bản vá nằm trong node_modules nên `npm update` sẽ ghi đè. Chạy lại script
    này sau mỗi lần cập nhật MeshCentral.

.PARAMETER Action
    apply   — áp bản vá (giữ bản gốc thành meshuser.js.orig)
    revert  — trả lại bản gốc
    status  — xem đã vá chưa

.EXAMPLE
    .\mesh-patch.ps1 status
    .\mesh-patch.ps1 apply
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('apply', 'revert', 'status')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'

$MeshRoot = 'D:\App\MeshCentral'
$TargetFile = Join-Path $MeshRoot 'node_modules\meshcentral\meshuser.js'
$BackupFile = "$TargetFile.orig"
$ServiceName = 'meshcentral.exe'

$Marker = 'SUA CUC BO: uu tien agentaliasdns'

$OriginalLine = '                name: domain.dns ? domain.dns : parent.certificates.CommonName,'

$PatchedBlock = @'
                // SUA CUC BO: uu tien agentaliasdns cho ten server.
                //
                // Mac dinh lay CommonName cua chung chi = ten tailnet, chi phan
                // giai duoc tu may da cai Tailscale. Lenh cai agent sinh ra tu
                // do se hong tren may ngoai tailnet: wget bao
                // "Name or service not known".
                //
                // serverinfo.name chi duoc dung o 8 cho trong default.handlebars,
                // tat ca deu la lenh cai agent hoac URL de agent ket noi vao —
                // dung thu can dia chi cong khai.
                //
                // Ban goc: meshuser.js.orig. Chay lai scripts/mesh-patch.ps1
                // sau moi lan cap nhat MeshCentral (npm ghi de node_modules).
                name: (typeof args.agentaliasdns == 'string') ? args.agentaliasdns : (domain.dns ? domain.dns : parent.certificates.CommonName),
'@

function Write-Step($Message) { Write-Host "  $Message" -ForegroundColor Gray }
function Write-Good($Message) { Write-Host "  $Message" -ForegroundColor Green }
function Write-Warn($Message) { Write-Host "  $Message" -ForegroundColor Yellow }
function Write-Bad($Message)  { Write-Host "  $Message" -ForegroundColor Red }

function Test-Patched {
    if (-not (Test-Path $TargetFile)) { return $false }
    return (Select-String -Path $TargetFile -Pattern $Marker -Quiet -ErrorAction SilentlyContinue) -eq $true
}

function Restart-Mesh {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Warn 'Không thấy service MeshCentral — khởi động lại thủ công.'
        return
    }
    Restart-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 15

    if (Get-NetTCPConnection -State Listen -LocalPort 4430 -ErrorAction SilentlyContinue) {
        Write-Good 'MeshCentral đã chạy lại, cổng 4430 lắng nghe'
    } else {
        Write-Bad 'MeshCentral chạy lại nhưng cổng 4430 KHÔNG lắng nghe'
        Write-Host '  Xem docs/services.md — "MeshCentral im lặng sau reboot".'
    }
}

# ---------------------------------------------------------------- status

if ($Action -eq 'status') {
    Write-Host "`nBản vá MeshCentral`n" -ForegroundColor Cyan

    if (-not (Test-Path $TargetFile)) {
        Write-Bad "Không thấy $TargetFile"
        Write-Host ''
        exit 1
    }

    if (Test-Patched) {
        Write-Good 'Đã vá — lệnh cài agent dùng tên miền công khai'
    } else {
        Write-Warn 'CHƯA vá — lệnh cài agent sẽ dùng tên tailnet'
        Write-Host '  Máy ngoài tailnet chạy lệnh đó sẽ gặp "Name or service not known".'
        Write-Host '  Vá bằng: .\mesh-patch.ps1 apply'
    }

    if (Test-Path $BackupFile) {
        Write-Step "Bản gốc: $BackupFile"
    }

    # Bản vá chỉ có tác dụng khi config đã khai agentaliasdns.
    $configFile = Join-Path $MeshRoot 'meshcentral-data\config.json'
    if (Test-Path $configFile) {
        if (Select-String -Path $configFile -Pattern '"agentaliasdns"' -Quiet -ErrorAction SilentlyContinue) {
            Write-Step 'config.json có khai agentaliasdns'
        } else {
            Write-Bad 'config.json CHƯA khai agentaliasdns — bản vá sẽ không có tác dụng'
        }
    }

    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------- apply

if ($Action -eq 'apply') {
    Write-Host "`nÁp bản vá`n" -ForegroundColor Cyan

    if (-not (Test-Path $TargetFile)) {
        Write-Bad "Không thấy $TargetFile"
        exit 1
    }

    if (Test-Patched) {
        Write-Good 'Đã vá rồi — không làm gì.'
        Write-Host ''
        exit 0
    }

    $content = Get-Content $TargetFile -Raw
    if ($content -notmatch [regex]::Escape($OriginalLine)) {
        Write-Bad 'Không tìm thấy dòng cần sửa.'
        Write-Host '  Có thể MeshCentral đã đổi phiên bản — kiểm tra lại thủ công:'
        Write-Host '    meshuser.js, tìm "name: domain.dns ? domain.dns :"'
        Write-Host ''
        exit 1
    }

    # Giữ bản gốc để revert được, và để so sánh sau mỗi lần cập nhật.
    if (-not (Test-Path $BackupFile)) {
        Copy-Item $TargetFile $BackupFile
        Write-Step "Đã lưu bản gốc: $BackupFile"
    }

    $content = $content.Replace($OriginalLine, $PatchedBlock.TrimEnd())
    Set-Content -Path $TargetFile -Value $content -NoNewline -Encoding UTF8

    # Cú pháp hỏng thì MeshCentral không khởi động nổi — kiểm tra trước khi restart.
    & node --check $TargetFile
    if ($LASTEXITCODE -ne 0) {
        Write-Bad 'Bản vá làm hỏng cú pháp JavaScript — trả lại bản gốc.'
        Copy-Item $BackupFile $TargetFile -Force
        exit 1
    }
    Write-Step 'Cú pháp JavaScript hợp lệ'

    Restart-Mesh

    Write-Host ''
    Write-Good 'Xong. Mở lại giao diện MeshCentral và lấy lệnh cài agent mới.'
    Write-Host '  Lệnh giờ dùng tên miền công khai, chạy được trên máy ngoài tailnet.'
    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------- revert

if ($Action -eq 'revert') {
    Write-Host "`nTrả lại bản gốc`n" -ForegroundColor Cyan

    if (-not (Test-Path $BackupFile)) {
        Write-Bad "Không có bản gốc để trả lại ($BackupFile)"
        exit 1
    }

    Copy-Item $BackupFile $TargetFile -Force
    Write-Step 'Đã trả lại meshuser.js'

    Restart-Mesh

    Write-Host ''
    exit 0
}
