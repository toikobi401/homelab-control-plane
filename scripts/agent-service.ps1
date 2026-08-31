<#
.SYNOPSIS
    Cài, gỡ, hoặc xem trạng thái Device Hub Agent như một Windows Service.

.DESCRIPTION
    CONTEXT.md §3 yêu cầu agent chạy như Windows Service, khởi động cùng máy.
    Script này bọc `sc.exe` để không phải nhớ cú pháp của nó.

    Cần chạy PowerShell với quyền Administrator — đăng ký service là thao tác
    toàn máy.

.PARAMETER Action
    set-secret — chép khoá chung từ user-secrets của Hub.Api sang biến môi
                 trường cấp máy, rồi khởi động lại service. Chạy cái này TRƯỚC
                 khi install. Không phải gõ hay dán khoá.
    install    — publish agent rồi đăng ký service, đặt tự khởi động
    uninstall  — dừng và xoá service (không xoá file đã publish)
    status     — xem service có tồn tại, đang chạy hay không
    restart    — dừng rồi chạy lại, dùng sau khi cập nhật cấu hình

.PARAMETER InstallPath
    Thư mục chứa bản publish. Mặc định C:\ProgramData\DeviceHub\Agent.
    Không đặt trong thư mục repo: repo có thể bị xoá hoặc đổi nhánh, còn
    service thì trỏ vào một đường dẫn cố định.

.EXAMPLE
    .\agent-service.ps1 set-secret
    .\agent-service.ps1 install
    .\agent-service.ps1 status
    .\agent-service.ps1 uninstall

.NOTES
    Khoá chung KHÔNG nằm trong script này và cũng không phải gõ tay: dùng
    `set-secret` để chép thẳng từ user-secrets của Hub.Api sang biến môi trường
    cấp máy. Xem docs/agent-setup.md.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('install', 'uninstall', 'status', 'restart', 'set-secret', 'test-lock')]
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

# 'status' và 'test-lock' chỉ đọc, không đổi gì ở cấp máy. Các hành động còn lại
# đăng ký/gỡ service hoặc ghi biến môi trường Machine — đều cần Administrator.
if ($Action -notin @('status', 'test-lock') -and -not (Test-Admin)) {
    Write-Error @"
Cần quyền Administrator để '$Action'.

Mở PowerShell bằng "Run as administrator" rồi chạy lại.
"@
    exit 1
}

switch ($Action) {

    'set-secret' {
        # Chép khoá thẳng từ user-secrets sang biến môi trường cấp máy.
        # Không in khoá ra màn hình và không bắt người dùng gõ lại — gõ tay là
        # nguồn sai lầm đã xảy ra thật (dán nguyên chỗ giữ chỗ trong hướng dẫn).
        $apiProject = Join-Path $RepoRoot 'backend\Hub.Api\Hub.Api.csproj'
        if (-not (Test-Path $apiProject)) {
            Write-Error "Không tìm thấy $apiProject — chạy script từ trong repo."
            exit 1
        }

        Write-Host "==> Đọc khoá từ user-secrets của Hub.Api..." -ForegroundColor Cyan

        # user-secrets nằm trong hồ sơ NGƯỜI DÙNG. Nếu mở PowerShell bằng
        # "Run as administrator" thì đây vẫn là hồ sơ của bạn, nên đọc được.
        $secretsOutput = dotnet user-secrets list --project $apiProject 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Không đọc được user-secrets:`n$secretsOutput"
            exit 1
        }

        $line = $secretsOutput | Where-Object { $_ -match '^Agent:SharedSecret\s*=\s*(.+)$' } | Select-Object -First 1
        if (-not $line) {
            Write-Error @"
Không tìm thấy 'Agent:SharedSecret' trong user-secrets của Hub.Api.

Hub chưa có khoá thì agent không thể khớp với nó. Sinh khoá mới:

  cd backend\Hub.Api
  dotnet user-secrets set "Agent:SharedSecret" "`$([Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 })))"
"@
            exit 1
        }

        $secret = ($line -replace '^Agent:SharedSecret\s*=\s*', '').Trim()

        # Header HTTP chỉ nhận ASCII in được. Khoá có ký tự lạ (ví dụ chữ tiếng
        # Việt do dán nhầm chỗ giữ chỗ) sẽ làm request hỏng trước khi gửi —
        # bắt tại đây để lỗi hiện ra ngay, không phải lúc bấm nút tắt máy.
        if ($secret -notmatch '^[\x21-\x7E]+$') {
            Write-Error "Khoá trong user-secrets chứa ký tự không hợp lệ cho header HTTP. Sinh lại khoá mới."
            exit 1
        }

        [Environment]::SetEnvironmentVariable('Agent__SharedSecret', $secret, 'Machine')

        Write-Host "Đã đặt Agent__SharedSecret (scope Machine), $($secret.Length) ký tự." -ForegroundColor Green

        # Tiến trình chỉ đọc biến môi trường lúc khởi động.
        if (Get-AgentService) {
            Write-Host "==> Khởi động lại service để nhận khoá mới..." -ForegroundColor Cyan
            Restart-Service -Name $ServiceName -Force
            Write-Host "Trạng thái: $((Get-AgentService).Status)" -ForegroundColor Green
        }
        else {
            Write-Host "Service chưa cài — cài bằng: .\agent-service.ps1 install" -ForegroundColor Yellow
        }

        Write-Host ""
        Write-Host "Kiểm chứng khoá đã khớp:" -ForegroundColor Yellow
        Write-Host "  .\agent-service.ps1 test-lock"
    }

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

    'test-lock' {
        # Kiểm chứng khoá bằng một lệnh thật. Chọn 'Lock' vì nó là hành động
        # nhẹ nhất trong nhóm A: khoá màn hình, mở lại bằng mật khẩu Windows —
        # không tắt máy, không mất việc đang làm.
        $key = [Environment]::GetEnvironmentVariable('Agent__SharedSecret', 'Machine')
        if ([string]::IsNullOrWhiteSpace($key)) {
            Write-Error "Chưa đặt Agent__SharedSecret. Chạy: .\agent-service.ps1 set-secret"
            exit 1
        }

        if ($key -notmatch '^[\x21-\x7E]+$') {
            Write-Error @"
Khoá chứa ký tự không hợp lệ cho header HTTP (dài $($key.Length) ký tự).

Gần như chắc chắn là đã dán nhầm chỗ giữ chỗ trong hướng dẫn. Sửa bằng:
  .\agent-service.ps1 set-secret
"@
            exit 1
        }

        # Gửi một hành động KHÔNG hợp lệ. Nghe ngược đời, nhưng đây mới là phép
        # thử đúng cho việc kiểm chứng khoá:
        #
        #   - Agent kiểm tra khoá TRƯỚC khi parse hành động (xem Program.cs).
        #     Sai khoá -> 401. Đúng khoá + hành động rác -> 400.
        #   - Nên 400 chứng minh khoá khớp mà KHÔNG thực thi gì cả.
        #
        # Bản trước gửi 'Lock' thật và nhận 500: service chạy dưới LocalSystem ở
        # Session 0, không có desktop tương tác nào để khoá. Khoá vẫn đúng, chỉ
        # là phép thử sai — xem PROGRESS.md ngày 2026-08-31.
        Write-Host "Kiểm tra khoá (không thực thi lệnh nào)..." -ForegroundColor Cyan

        # -SkipHttpErrorCheck: giữ nguyên response để đọc mã và nội dung lỗi.
        # Không có nó thì PowerShell ném exception và body đã bị dispose.
        $response = Invoke-WebRequest -UseBasicParsing -SkipHttpErrorCheck -Method Post `
            -Uri 'http://127.0.0.1:5199/agent/power' `
            -Headers @{ Authorization = "Bearer $key" } `
            -ContentType 'application/json' `
            -Body '{"action":"__probe__"}' `
            -TimeoutSec 10 `
            -ErrorAction Stop

        $status = [int]$response.StatusCode

        Write-Host ""
        switch ($status) {
            400 {
                Write-Host "Khoá KHỚP. Agent xác thực thành công (HTTP 400 = từ chối hành động thử, đúng như mong đợi)." -ForegroundColor Green
                Write-Host ""
                Write-Host "Lưu ý: agent chạy dưới LocalSystem ở Session 0 nên KHÔNG khoá/ngủ được" -ForegroundColor Yellow
                Write-Host "màn hình của bạn. Shutdown và Restart thì vẫn chạy — xem docs/agent-setup.md."
            }
            401 {
                Write-Error "HTTP 401 — khoá KHÔNG khớp. Sửa bằng: .\agent-service.ps1 set-secret"
                exit 1
            }
            503 {
                Write-Error "HTTP 503 — agent chưa thấy khoá nào. Chạy: .\agent-service.ps1 set-secret"
                exit 1
            }
            default {
                # Không đoán. In nguyên mã và nội dung agent trả về.
                $body = [System.Text.Encoding]::UTF8.GetString($response.Content)
                Write-Error "HTTP $status — không nằm trong các trường hợp đã biết.`n`nAgent trả về:`n$body"
                exit 1
            }
        }
    }
}
