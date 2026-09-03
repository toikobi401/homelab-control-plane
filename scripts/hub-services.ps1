<#
.SYNOPSIS
    Cài Hub và Cloudflare Tunnel thành Windows Service, để chúng tự chạy sau khi khởi động máy.

.DESCRIPTION
    Sau khi restart máy, hub và cloudflared không tự chạy lại vì chúng chạy thủ
    công. Kết quả: Cloudflare trả "Error 1033" — không có tunnel nào kết nối tới.

    Script này đăng ký cả hai thành service đặt chế độ tự khởi động.

    MeshCentral KHÔNG có trong script này — nó đã là service sẵn
    (`meshcentral.exe`, cài bằng `node node_modules/meshcentral --install`).

    Cảnh báo về cổng: MeshCentral thấy cổng 4430 bị chiếm thì lặng lẽ nhảy sang
    4431 (webserver.js:9294 `CheckListenPort(port + 1, ...)`) và không báo lỗi
    gì. Chạy một bản MeshCentral thứ hai bằng tay sẽ khiến service tụt xuống
    cổng khác, tunnel trỏ vào 4430 thì không thấy gì. Đã gặp thật.
    Đừng chạy `node node_modules/meshcentral` bằng tay khi service đang chạy.

.PARAMETER Action
    install    — đăng ký cả hai service, đặt tự khởi động, rồi chạy
    uninstall  — dừng và gỡ cả hai service
    status     — xem trạng thái ba service (kể cả MeshCentral)
    restart    — chạy lại cả hai, dùng sau khi cập nhật cấu hình

.PARAMETER Only
    Chỉ tác động một service: 'hub' hoặc 'cloudflared'.

.EXAMPLE
    .\hub-services.ps1 status
    .\hub-services.ps1 install
    .\hub-services.ps1 restart -Only hub

.NOTES
    install/uninstall/restart cần quyền Administrator — đăng ký service là
    thao tác cấp máy.

    Chạy `dotnet publish` TRƯỚC khi install: service chạy Hub.Api.exe chứ không
    chạy được `dotnet run`.

        dotnet publish backend/Hub.Api/Hub.Api.csproj -c Release `
            -o backend/Hub.Api/bin/Release/net10.0/publish

    Sau khi sửa code hub thì publish lại rồi `restart -Only hub`.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('install', 'uninstall', 'status', 'restart')]
    [string]$Action = 'status',

    [ValidateSet('hub', 'cloudflared')]
    [string]$Only
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

$HubServiceName = 'HubApi'
$HubDisplayName = 'Device Hub API'
$HubPublishDir = Join-Path $RepoRoot 'backend\Hub.Api\bin\Release\net10.0\publish'
$HubExe = Join-Path $HubPublishDir 'Hub.Api.exe'

# Thư mục tạm khi publish lúc service đang giữ file exe. Script sẽ dừng service
# rồi tráo vào đúng chỗ — xem Sync-HubPublish.
$HubStagingDir = Join-Path $RepoRoot 'backend\Hub.Api\bin\Release\net10.0\publish-new'

# Thư mục dữ liệu (hub.db, chứng chỉ). PHẢI khai tường minh: service chạy dưới
# LocalSystem, mà mặc định HubPaths dùng LocalApplicationData — với LocalSystem
# đó là C:\Windows\System32\config\systemprofile\AppData\Local, không phải thư
# mục của người dùng. Không khai thì hub thấy thư mục trống và tưởng lần đầu
# chạy, đòi đặt lại mật khẩu trong khi dữ liệu cũ vẫn còn nguyên chỗ khác.
#
# Đặt ở ổ D theo hiện trạng máy này (ổ C chật, MeshCentral cũng ở D).
$HubDataDir = 'D:\App\HubData'

# Tunnel: hub nhận HTTP trên loopback 7190, TLS kết thúc ở biên Cloudflare.
# Xem Hosting/NetworkBinding.cs và CONTEXT.md §4a.
$HubBindMode = 'Tunnel'

$CfServiceName = 'Cloudflared'
$CloudflaredExe = 'C:\Program Files (x86)\cloudflared\cloudflared.exe'

# Service chạy dưới LocalSystem, không thấy được C:\Users\<tên>\.cloudflared.
# cloudflared tìm config trong thư mục .cloudflared của chính tài khoản đang
# chạy, nên config phải nằm ở hồ sơ hệ thống.
$CfUserConfigDir = Join-Path $env:USERPROFILE '.cloudflared'
$CfSystemConfigDir = 'C:\Windows\System32\config\systemprofile\.cloudflared'

$MeshServiceName = 'meshcentral.exe'

function Write-Step($Message) { Write-Host "  $Message" -ForegroundColor Gray }
function Write-Good($Message) { Write-Host "  $Message" -ForegroundColor Green }
function Write-Warn($Message) { Write-Host "  $Message" -ForegroundColor Yellow }
function Write-Bad($Message)  { Write-Host "  $Message" -ForegroundColor Red }

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)
}

function Show-One($Name, $Label) {
    $service = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Warn "$Label : chưa cài"
        return
    }
    $line = "$Label : $($service.State), khởi động $($service.StartMode)"
    if ($service.State -eq 'Running' -and $service.StartMode -eq 'Auto') {
        Write-Good $line
    } else {
        Write-Warn $line
    }
}

function Show-Port($Port, $Label) {
    $listening = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
    if ($listening) {
        Write-Good "$Label (cổng $Port) : đang lắng nghe"
    } else {
        Write-Bad "$Label (cổng $Port) : KHÔNG lắng nghe"
    }
}

function Assert-Admin {
    if (Test-Admin) { return }
    Write-Host ''
    Write-Warn "$Action cần quyền Administrator."
    Write-Host "  Mở PowerShell bằng Run as Administrator rồi chạy lại.`n"
    exit 1
}

function Should-Do($Which) {
    return (-not $Only) -or ($Only -eq $Which)
}

<#
    Dừng hub dứt điểm, kể cả khi service kẹt ở "Start Pending".

    Bản hub thiếu UseWindowsService() không bao giờ báo "đã sẵn sàng" cho
    Service Control Manager, nên Stop-Service treo tới khi hết giờ. Giết thẳng
    tiến trình là cách duy nhất thoát khỏi trạng thái đó.
#>
function Stop-HubCompletely {
    $service = Get-Service -Name $HubServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        # Bọc try vì chính lệnh này là cái treo khi service kẹt.
        try {
            Stop-Service -Name $HubServiceName -Force -ErrorAction Stop -WarningAction SilentlyContinue
        } catch {
            Write-Step 'Hub: Stop-Service không xong (service kẹt), giết tiến trình'
        }
    }

    Get-Process Hub.Api -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

<#
    Tráo bản publish mới vào chỗ service đang dùng.

    Cần thiết vì không publish đè lên file exe mà service đang giữ được. Quy
    trình: publish ra publish-new, dừng service, tráo, chạy lại.
#>
function Sync-HubPublish {
    if (-not (Test-Path $HubStagingDir)) { return }

    Write-Step 'Hub: thấy bản publish mới, đang tráo vào'
    Stop-HubCompletely

    if (Test-Path $HubPublishDir) {
        Remove-Item $HubPublishDir -Recurse -Force
    }
    Move-Item $HubStagingDir $HubPublishDir
    Write-Step 'Hub: đã dùng bản publish mới'
}

# ---------------------------------------------------------------- status

if ($Action -eq 'status') {
    Write-Host "`nDịch vụ của Hub`n" -ForegroundColor Cyan

    Show-One $HubServiceName  'Hub API      '
    Show-One $CfServiceName   'Cloudflared  '
    Show-One $MeshServiceName 'MeshCentral  '

    $dataDir = [Environment]::GetEnvironmentVariable('HUB_DATA_DIR', 'Machine')
    if ($dataDir) {
        if (Test-Path (Join-Path $dataDir 'hub.db')) {
            Write-Good "Dữ liệu     : $dataDir"
        } else {
            Write-Bad "Dữ liệu     : $dataDir (KHÔNG có hub.db)"
        }
        if (-not (Test-Path (Join-Path $dataDir 'appsettings.Production.json'))) {
            Write-Bad 'Cấu hình    : thiếu appsettings.Production.json'
            Write-Host '  /devices sẽ trống và /remote không nhúng được MeshCentral.'
        }
    } else {
        Write-Bad 'Dữ liệu     : chưa đặt HUB_DATA_DIR'
        Write-Host '  Service chạy dưới LocalSystem sẽ dùng systemprofile và tưởng lần đầu chạy.'
    }

    Write-Host ''
    Show-Port 7190 'Hub        '
    Show-Port 4430 'MeshCentral'

    # 4431 là dấu hiệu có hai bản MeshCentral cùng chạy — bản thứ hai bị đẩy
    # sang cổng kế tiếp và tunnel sẽ không thấy nó.
    if (Get-NetTCPConnection -State Listen -LocalPort 4431 -ErrorAction SilentlyContinue) {
        Write-Host ''
        Write-Bad 'Cổng 4431 đang mở — có HAI bản MeshCentral cùng chạy.'
        Write-Host '  Bản bị đẩy sang 4431 sẽ không nhận được kết nối qua tunnel.'
        Write-Host '  Dừng bản chạy tay, rồi: Restart-Service meshcentral.exe'
    }

    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------- install

if ($Action -eq 'install') {
    Assert-Admin
    Write-Host "`nCài dịch vụ`n" -ForegroundColor Cyan

    if (Should-Do 'hub') {
        if ((-not (Test-Path $HubExe)) -and (-not (Test-Path $HubStagingDir))) {
            Write-Bad "Chưa có bản publish: $HubExe"
            Write-Host ''
            Write-Host '  Chạy trước:'
            Write-Host '    dotnet publish backend/Hub.Api/Hub.Api.csproj -c Release \'
            Write-Host '        -o backend/Hub.Api/bin/Release/net10.0/publish'
            Write-Host ''
            exit 1
        }

        # Bản chạy tay (hoặc service cũ) giữ cổng 7190 thì service khởi động sẽ
        # chết vì "address already in use".
        Stop-HubCompletely
        Sync-HubPublish

        $existing = Get-Service -Name $HubServiceName -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Step 'Hub: service đã có, gỡ để cài lại'
            Stop-Service -Name $HubServiceName -Force -ErrorAction SilentlyContinue
            & sc.exe delete $HubServiceName | Out-Null
            Start-Sleep -Seconds 2
        }

        # binPath cần dấu nháy quanh đường dẫn (có khoảng trắng), và khoảng
        # trắng sau "binPath=" là bắt buộc với sc.exe.
        & sc.exe create $HubServiceName binPath= "`"$HubExe`"" start= auto DisplayName= "$HubDisplayName" | Out-Null
        & sc.exe description $HubServiceName "Backend cua Device Hub - phuc vu API va giao dien web" | Out-Null

        # Bind mode đọc từ biến môi trường (NetworkBinding.ModeKey). Service
        # không thừa hưởng biến của phiên đăng nhập nên phải đặt ở cấp máy.
        [Environment]::SetEnvironmentVariable('HUB_BIND_MODE', $HubBindMode, 'Machine')
        Write-Step "Hub: HUB_BIND_MODE=$HubBindMode (cấp máy)"

        if (-not (Test-Path $HubDataDir)) {
            New-Item -ItemType Directory -Path $HubDataDir -Force | Out-Null
        }
        [Environment]::SetEnvironmentVariable('HUB_DATA_DIR', $HubDataDir, 'Machine')
        Write-Step "Hub: HUB_DATA_DIR=$HubDataDir (cấp máy)"

        if (-not (Test-Path (Join-Path $HubDataDir 'hub.db'))) {
            Write-Warn 'Hub: chưa có hub.db ở thư mục này — hub sẽ hỏi đặt mật khẩu mới.'
            Write-Host  '  Có dữ liệu cũ thì chép hub.db vào trước khi mở giao diện.'
        }

        # Bí mật (token Tailscale, địa chỉ MeshCentral) nằm trong user-secrets,
        # mà user-secrets gắn với hồ sơ người dùng — LocalSystem không đọc được.
        # Thiếu chúng thì /devices trống và /remote không biết nhúng gì.
        $hubConfigFile = Join-Path $HubDataDir 'appsettings.Production.json'
        if (-not (Test-Path $hubConfigFile)) {
            Write-Warn "Hub: chưa có $hubConfigFile"
            Write-Host  '  /devices sẽ trống và /remote không nhúng được MeshCentral.'
            Write-Host  '  Xem docs/services.md — mục "Thiết bị biến mất sau khi cài service".'
        } else {
            # File chứa token Tailscale. Thư mục trên ổ D mặc định cho Users đọc,
            # nên bỏ thừa kế và chỉ giữ SYSTEM, Administrators, chủ sở hữu.
            $acl = Get-Acl $hubConfigFile
            $acl.SetAccessRuleProtection($true, $false)
            foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRule($rule) | Out-Null }
            foreach ($who in 'NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators', "$env:USERDOMAIN\$env:USERNAME") {
                try {
                    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($who, 'FullControl', 'Allow')))
                } catch {
                    Write-Step "Bỏ qua quyền cho ${who}: $($_.Exception.Message)"
                }
            }
            Set-Acl -Path $hubConfigFile -AclObject $acl
            Write-Step 'Hub: đã siết quyền đọc appsettings.Production.json'
        }

        # Tự chạy lại khi sập: sau 5s, 10s, rồi 30s. Bộ đếm reset sau 1 ngày.
        & sc.exe failure $HubServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

        Start-Service -Name $HubServiceName
        Write-Good 'Hub: đã cài và chạy'
    }

    if (Should-Do 'cloudflared') {
        if (-not (Test-Path $CloudflaredExe)) {
            Write-Bad "Không thấy cloudflared: $CloudflaredExe"
            exit 1
        }

        if (-not (Test-Path (Join-Path $CfUserConfigDir 'config.yml'))) {
            Write-Bad "Không thấy config: $CfUserConfigDir\config.yml"
            exit 1
        }

        Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2

        # Chép config và credentials sang hồ sơ hệ thống — xem chú thích ở
        # $CfSystemConfigDir.
        if (-not (Test-Path $CfSystemConfigDir)) {
            New-Item -ItemType Directory -Path $CfSystemConfigDir -Force | Out-Null
        }
        Copy-Item (Join-Path $CfUserConfigDir 'config.yml') $CfSystemConfigDir -Force
        Copy-Item (Join-Path $CfUserConfigDir '*.json') $CfSystemConfigDir -Force
        Write-Step 'Cloudflared: đã chép config sang hồ sơ hệ thống'

        if (Get-Service -Name $CfServiceName -ErrorAction SilentlyContinue) {
            Stop-Service -Name $CfServiceName -Force -ErrorAction SilentlyContinue
            & sc.exe delete $CfServiceName | Out-Null
            Start-Sleep -Seconds 2
            Write-Step 'Cloudflared: gỡ service cũ để cài lại'
        }

        # KHÔNG dùng `cloudflared service install`: nó tạo service với binPath
        # chỉ có đường dẫn exe, thiếu `tunnel run`. Service khởi động lên chỉ in
        # trợ giúp rồi thoát, trạng thái Stopped mà không có lỗi gì rõ ràng.
        # Đã gặp thật.
        $cfConfig = Join-Path $CfSystemConfigDir 'config.yml'
        $cfBinPath = '"' + $CloudflaredExe + '" --config "' + $cfConfig + '" tunnel run'

        # New-Service chứ không sc.exe: PowerShell tách chuỗi có khoảng trắng
        # thành nhiều tham số trước khi sc.exe nhận được, gây lỗi 1639.
        New-Service -Name $CfServiceName `
            -BinaryPathName $cfBinPath `
            -DisplayName 'Cloudflare Tunnel' `
            -Description 'Cloudflare Tunnel cho Device Hub' `
            -StartupType Automatic | Out-Null

        & sc.exe failure $CfServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

        Start-Service -Name $CfServiceName
        Write-Good 'Cloudflared: đã cài và chạy'
    }

    Write-Host ''
    Write-Good 'Xong. Kiểm tra: .\hub-services.ps1 status'
    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------- uninstall

if ($Action -eq 'uninstall') {
    Assert-Admin
    Write-Host "`nGỡ dịch vụ`n" -ForegroundColor Cyan

    if (Should-Do 'hub') {
        if (Get-Service -Name $HubServiceName -ErrorAction SilentlyContinue) {
            Stop-Service -Name $HubServiceName -Force -ErrorAction SilentlyContinue
            & sc.exe delete $HubServiceName | Out-Null
            Write-Good 'Hub: đã gỡ'
        } else {
            Write-Step 'Hub: không có service để gỡ'
        }
        # Biến môi trường cấp máy để lại cũng vô hại, nhưng dọn cho sạch.
        [Environment]::SetEnvironmentVariable('HUB_BIND_MODE', $null, 'Machine')
        # HUB_DATA_DIR để lại: gỡ nó đi thì lần cài sau hub quay về thư mục mặc
        # định và lại tưởng lần đầu chạy. Dữ liệu trong thư mục đó không bị đụng.
        Write-Step "Hub: giữ HUB_DATA_DIR=$HubDataDir và dữ liệu trong đó"
    }

    if (Should-Do 'cloudflared') {
        if (Get-Service -Name $CfServiceName -ErrorAction SilentlyContinue) {
            Stop-Service -Name $CfServiceName -Force -ErrorAction SilentlyContinue
            & sc.exe delete $CfServiceName | Out-Null
            Write-Good 'Cloudflared: đã gỡ'
        } else {
            Write-Step 'Cloudflared: không có service để gỡ'
        }
    }

    Write-Host "`n  MeshCentral không bị đụng tới — nó là service riêng.`n"
    exit 0
}

# ---------------------------------------------------------------- restart

if ($Action -eq 'restart') {
    Assert-Admin
    Write-Host "`nChạy lại dịch vụ`n" -ForegroundColor Cyan

    # Publish lại rồi restart là luồng thường gặp nhất sau khi sửa code.
    if ((Should-Do 'hub') -and (Test-Path $HubStagingDir)) { Sync-HubPublish }

    foreach ($pair in @(@{ Which = 'hub'; Name = $HubServiceName; Label = 'Hub' },
                        @{ Which = 'cloudflared'; Name = $CfServiceName; Label = 'Cloudflared' })) {
        if (-not (Should-Do $pair.Which)) { continue }

        if (Get-Service -Name $pair.Name -ErrorAction SilentlyContinue) {
            Restart-Service -Name $pair.Name -Force
            Write-Good "$($pair.Label): đã chạy lại"
        } else {
            Write-Warn "$($pair.Label): chưa cài service"
        }
    }

    Write-Host ''
    exit 0
}
