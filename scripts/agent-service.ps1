<#
.SYNOPSIS
    Cài, gỡ, hoặc xem trạng thái Device Hub Agent như một Windows Service.

.DESCRIPTION
    CONTEXT.md §3 yêu cầu agent chạy như Windows Service, khởi động cùng máy.
    Script này bọc `sc.exe` để không phải nhớ cú pháp của nó.

    Cần chạy PowerShell với quyền Administrator — đăng ký service là thao tác
    toàn máy.

.PARAMETER Action
    install   — publish agent rồi đăng ký service, đặt tự khởi động
    uninstall — dừng và xoá service (không xoá file đã publish)
    status    — xem service có tồn tại, đang chạy hay không
    restart   — dừng rồi chạy lại, dùng sau khi cập nhật cấu hình

.PARAMETER InstallPath
    Thư mục chứa bản publish. Mặc định C:\ProgramData\DeviceHub\Agent.
    Không đặt trong thư mục repo: repo có thể bị xoá hoặc đổi nhánh, còn
    service thì trỏ vào một đường dẫn cố định.

.EXAMPLE
    .\agent-service.ps1 install
    .\agent-service.ps1 status
    .\agent-service.ps1 uninstall

.NOTES
    Khoá chung (Agent:SharedSecret) KHÔNG nằm trong script này. Đặt nó bằng
    biến môi trường cấp máy trước khi cài — xem docs/agent-setup.md.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('install', 'uninstall', 'status', 'restart')]
    [string]$Action,

    [string]$InstallPath = 'C:\ProgramData\DeviceHub\Agent'
)

$ErrorActionPreference = 'Stop'

# Tên phải khớp ServiceName trong Program.cs, nếu không thì Event Log ghi một
# đằng mà service tên một nẻo.
$ServiceName = 'Device Hub Agent'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$AgentProject = Join-Path $RepoRoot 'backend\Hub.Agent\Hub.Agent.csproj'

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-AgentService {
    return Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

# Mọi hành động trừ 'status' đều đổi trạng thái toàn máy.
if ($Action -ne 'status' -and -not (Test-Admin)) {
    Write-Error @"
Cần quyền Administrator để '$Action'.

Mở PowerShell bằng "Run as administrator" rồi chạy lại.
"@
    exit 1
}

switch ($Action) {

    'status' {
        $service = Get-AgentService
        if (-not $service) {
            Write-Host "Service '$ServiceName' chưa được cài." -ForegroundColor Yellow
            Write-Host "Cài bằng: .\agent-service.ps1 install"
            exit 0
        }

        Write-Host "Tên      : $($service.Name)"
        Write-Host "Trạng thái: $($service.Status)"
        Write-Host "Khởi động : $($service.StartType)"

        # Đường dẫn thật của binary — hữu ích khi nghi service đang chạy bản cũ.
        $wmi = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
        if ($wmi) {
            Write-Host "Đường dẫn : $($wmi.PathName)"
        }

        Write-Host ""
        Write-Host "Xem log: Event Viewer → Windows Logs → Application → nguồn '$ServiceName'"
        Write-Host "Hoặc   : Get-EventLog -LogName Application -Source '$ServiceName' -Newest 20"
    }

    'install' {
        if (Get-AgentService) {
            Write-Error "Service '$ServiceName' đã tồn tại. Gỡ trước bằng: .\agent-service.ps1 uninstall"
            exit 1
        }

        Write-Host "==> Publish agent..." -ForegroundColor Cyan
        # Publish self-contained=false: máy đã có .NET runtime. Dùng Release để
        # service chạy bản tối ưu, không phải bản debug.
        dotnet publish $AgentProject --configuration Release --output $InstallPath
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Publish thất bại — không đăng ký service."
            exit 1
        }

        $exePath = Join-Path $InstallPath 'Hub.Agent.exe'
        if (-not (Test-Path $exePath)) {
            Write-Error "Không tìm thấy $exePath sau khi publish."
            exit 1
        }

        Write-Host "==> Đăng ký service..." -ForegroundColor Cyan
        # binPath cần dấu nháy vì đường dẫn có thể chứa dấu cách.
        # start=delayed-auto: hoãn một chút sau khi Windows khởi động, để mạng
        # và Tailscale kịp sẵn sàng — agent gọi hub ngay khi chạy.
        $result = sc.exe create "$ServiceName" binPath= "`"$exePath`"" start= delayed-auto DisplayName= "$ServiceName"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "sc.exe create thất bại: $result"
            exit 1
        }

        sc.exe description "$ServiceName" "Agent cua Personal Device Hub: bao danh va nhan lenh dieu khien nguon." | Out-Null

        # Lỗi thì tự chạy lại: sau 5s, 10s, rồi mỗi 60s. Bộ đếm reset sau 1 ngày.
        # Không có dòng này thì agent chết một lần là nằm im tới khi khởi động lại máy.
        sc.exe failure "$ServiceName" reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null

        Write-Host "==> Khởi động service..." -ForegroundColor Cyan
        Start-Service -Name $ServiceName

        $service = Get-AgentService
        Write-Host ""
        Write-Host "Xong. Trạng thái: $($service.Status)" -ForegroundColor Green
        Write-Host ""
        Write-Host "Kiểm chứng agent trả lời:" -ForegroundColor Yellow
        Write-Host "  curl http://127.0.0.1:5199/agent/health"
        Write-Host ""
        Write-Host "Nếu service chạy rồi tắt ngay, xem nguyên nhân ở Event Viewer:" -ForegroundColor Yellow
        Write-Host "  Get-EventLog -LogName Application -Source '$ServiceName' -Newest 20"
    }

    'uninstall' {
        $service = Get-AgentService
        if (-not $service) {
            Write-Host "Service '$ServiceName' không tồn tại — không có gì để gỡ." -ForegroundColor Yellow
            exit 0
        }

        if ($service.Status -ne 'Stopped') {
            Write-Host "==> Dừng service..." -ForegroundColor Cyan
            Stop-Service -Name $ServiceName -Force
        }

        Write-Host "==> Xoá service..." -ForegroundColor Cyan
        sc.exe delete "$ServiceName" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "sc.exe delete thất bại."
            exit 1
        }

        Write-Host ""
        Write-Host "Đã gỡ service." -ForegroundColor Green
        Write-Host "File publish vẫn còn ở: $InstallPath"
        Write-Host "Xoá thủ công nếu muốn: Remove-Item -Recurse '$InstallPath'"
    }

    'restart' {
        if (-not (Get-AgentService)) {
            Write-Error "Service '$ServiceName' chưa được cài."
            exit 1
        }

        Restart-Service -Name $ServiceName -Force
        Write-Host "Đã khởi động lại. Trạng thái: $((Get-AgentService).Status)" -ForegroundColor Green
    }
}
