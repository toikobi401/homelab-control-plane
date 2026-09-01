<#
.SYNOPSIS
    Chọn đường kết nối cho Mesh Agent: qua tailnet nếu tới được, không thì qua Cloudflare.

.DESCRIPTION
    MeshCentral sinh file .msh với MỘT địa chỉ server duy nhất — địa chỉ công
    khai (agentaliasdns). Máy đã ở trong tailnet vẫn đi vòng ra Internet rồi
    quay lại, dù có đường thẳng ngắn hơn.

    Script này dò xem máy có tới được MeshCentral qua tailnet không, rồi ghi
    ĐÚNG MỘT địa chỉ hợp lệ vào .msh.

    Vì sao không khai cả hai địa chỉ (MeshServer=A,B):
    MeshAgent có hỗ trợ danh sách ngăn cách bằng dấu phẩy, nhưng nó bốc NGẪU
    NHIÊN chứ không ưu tiên — agentcore.c:3894 `(rval % rs->NumResults) + 1`.
    Máy trong tailnet vẫn sẽ qua Cloudflare khoảng một nửa số lần. Đó là cân
    bằng tải, không phải thứ ta cần.

    Vì sao dò bằng kết nối thật chứ không chỉ hỏi `tailscale status`:
    Tailscale có thể đang chạy mà vẫn không tới được server — server tắt, ACL
    chặn, hoặc máy vừa đổi mạng. Tin vào trạng thái thay vì kết quả sẽ khoá máy
    vào một đường chết, và agent im lặng không báo gì.

.PARAMETER Action
    detect  — chỉ xem máy này nên đi đường nào, không sửa gì
    apply   — dò rồi ghi vào .msh và khởi động lại agent
    status  — xem .msh hiện đang trỏ đâu

.PARAMETER Force
    Ép một đường cụ thể: tailnet hoặc cloudflare. Bỏ qua bước dò.

.EXAMPLE
    .\mesh-agent-route.ps1 detect
    .\mesh-agent-route.ps1 apply
    .\mesh-agent-route.ps1 apply -Force cloudflare

.NOTES
    apply cần quyền Administrator: .msh nằm trong Program Files và phải khởi
    động lại service.

    Chạy lại script khi máy đổi mạng — vào tailnet lần đầu, hoặc rời tailnet.
    Đường đã ghi là cố định cho tới lần chạy sau.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('detect', 'apply', 'status')]
    [string]$Action = 'detect',

    [ValidateSet('tailnet', 'cloudflare')]
    [string]$Force
)

$ErrorActionPreference = 'Stop'

# Hai địa chỉ của cùng một MeshCentral. Khớp với meshcentral-data/config.json:
# TailnetHost = settings.cert, CloudflareHost:Port = agentaliasdns:agentaliasport.
# Đổi ở đây thì phải đổi cả bên đó, nếu không agent trỏ vào chỗ không có server.
$TailnetHost    = 'hub.tailnet-example.ts.net'
$TailnetPort    = 4430
$CloudflareHost = 'mesh.youtubecontentgen.io.vn'
$CloudflarePort = 443

$MshPath = 'C:\Program Files\Mesh Agent\MeshAgent.msh'
$ServiceName = 'Mesh Agent'

function Write-Step($Message) { Write-Host "  $Message" -ForegroundColor Gray }
function Write-Good($Message) { Write-Host "  $Message" -ForegroundColor Green }
function Write-Warn($Message) { Write-Host "  $Message" -ForegroundColor Yellow }

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)
}

<#
    Máy này có tới được MeshCentral qua tailnet không.

    Ba bước, dừng ngay khi bước nào hỏng — mỗi bước loại một nguyên nhân khác
    nhau, nên biết hỏng ở đâu là biết phải sửa gì:
      1. Tailscale đang chạy chưa      -> chưa cài, hoặc chưa đăng nhập
      2. Tên MagicDNS phân giải được   -> chạy nhưng DNS chưa nhận
      3. Cổng agent thật sự mở         -> tới được máy nhưng MeshCentral tắt
#>
function Test-TailnetRoute {
    $tailscaleExe = 'C:\Program Files\Tailscale\tailscale.exe'
    if (-not (Test-Path $tailscaleExe)) {
        Write-Step 'Tailscale: chưa cài'
        return $false
    }

    try {
        $state = (& $tailscaleExe status --json 2>$null | ConvertFrom-Json).BackendState
    } catch {
        $state = $null
    }
    if ($state -ne 'Running') {
        $shown = if ($state) { $state } else { 'không đọc được' }
        Write-Step "Tailscale: có cài nhưng chưa chạy (trạng thái: $shown)"
        return $false
    }
    Write-Step 'Tailscale: đang chạy'

    try {
        $resolved = (Resolve-DnsName $TailnetHost -Type A -ErrorAction Stop).IPAddress | Select-Object -First 1
    } catch {
        Write-Step "MagicDNS: không phân giải được $TailnetHost"
        return $false
    }
    Write-Step "MagicDNS: $TailnetHost -> $resolved"

    # Chạm thật vào cổng agent. Tailscale chạy và DNS phân giải được vẫn có thể
    # không tới nơi — ACL của tailnet chặn, hoặc MeshCentral không lắng nghe.
    try {
        $probe = New-Object Net.Sockets.TcpClient
        $connect = $probe.BeginConnect($resolved, $TailnetPort, $null, $null)
        $reached = $connect.AsyncWaitHandle.WaitOne(3000, $false) -and $probe.Connected
        $probe.Close()
    } catch {
        $reached = $false
    }

    if (-not $reached) {
        Write-Step "Cổng $TailnetPort : không tới được (server tắt, hoặc ACL chặn)"
        return $false
    }

    Write-Step "Cổng $TailnetPort : mở"
    return $true
}

function Get-ServerUrl($Route) {
    if ($Route -eq 'tailnet') {
        return "wss://${TailnetHost}:${TailnetPort}/agent.ashx"
    }
    return "wss://${CloudflareHost}:${CloudflarePort}/agent.ashx"
}

function Get-CurrentUrl {
    if (-not (Test-Path $MshPath)) { return $null }
    $line = Get-Content $MshPath | Where-Object { $_ -match '^MeshServer=' } | Select-Object -First 1
    if (-not $line) { return $null }
    return $line -replace '^MeshServer=', ''
}

function Resolve-Route {
    if ($Force) {
        Write-Step "Ép đường: $Force (bỏ qua bước dò)"
        return $Force
    }
    if (Test-TailnetRoute) { return 'tailnet' }
    return 'cloudflare'
}

# ---------------------------------------------------------------- status

if ($Action -eq 'status') {
    Write-Host "`nMesh Agent — đường kết nối hiện tại`n" -ForegroundColor Cyan

    if (-not (Test-Path $MshPath)) {
        Write-Warn "Chưa cài agent (không thấy $MshPath)"
        Write-Host "`n  Cài agent từ MeshCentral trước, rồi chạy lại: .\mesh-agent-route.ps1 apply`n"
        exit 1
    }

    $current = Get-CurrentUrl
    if ($current -like "*$TailnetHost*") {
        Write-Good "Đang đi qua tailnet: $current"
    } elseif ($current -like "*$CloudflareHost*") {
        Write-Good "Đang đi qua Cloudflare: $current"
    } else {
        Write-Warn "Địa chỉ lạ: $current"
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Step "Service: $($service.Status)"
    } else {
        Write-Warn 'Service: không tồn tại'
    }
    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------- detect

Write-Host "`nDò đường kết nối tới MeshCentral`n" -ForegroundColor Cyan
$route = Resolve-Route
$targetUrl = Get-ServerUrl $route

Write-Host ''
# Khi ép bằng -Force thì chưa dò gì, nên không được nói "không tới được" —
# đó là kết luận của phép dò, không phải của lựa chọn thủ công.
if ($route -eq 'tailnet') {
    $reason = if ($Force) { 'do bạn chỉ định' } else { 'đường thẳng, không qua Internet' }
    Write-Good "Dùng: tailnet ($reason)"
} else {
    $reason = if ($Force) { 'do bạn chỉ định' } else { 'không tới được tailnet' }
    Write-Good "Dùng: Cloudflare ($reason)"
}
Write-Step $targetUrl

if ($Action -eq 'detect') {
    $current = Get-CurrentUrl
    Write-Host ''
    if (-not $current) {
        Write-Warn 'Chưa cài agent — chưa có gì để so sánh.'
    } elseif ($current -eq $targetUrl) {
        Write-Good 'Agent đã trỏ đúng đường này rồi, không cần sửa.'
    } else {
        Write-Warn "Agent đang trỏ: $current"
        Write-Host "`n  Sửa lại: .\mesh-agent-route.ps1 apply  (cần Administrator)"
    }
    Write-Host ''
    exit 0
}

# ---------------------------------------------------------------- apply

if (-not (Test-Admin)) {
    Write-Host ''
    Write-Warn 'apply cần quyền Administrator.'
    Write-Host "  Mở PowerShell bằng Run as Administrator rồi chạy lại.`n"
    exit 1
}

if (-not (Test-Path $MshPath)) {
    Write-Host ''
    Write-Warn "Chưa cài agent (không thấy $MshPath)"
    Write-Host "  Tải agent từ MeshCentral và cài trước đã.`n"
    exit 1
}

$current = Get-CurrentUrl
if ($current -eq $targetUrl) {
    Write-Host ''
    Write-Good 'Đã trỏ đúng đường rồi — không sửa gì.'
    Write-Host ''
    exit 0
}

# Giữ bản cũ. .msh chứa MeshID và ServerID — mất là agent mất luôn danh tính,
# phải cài lại từ đầu và máy hiện ra như một thiết bị mới.
$backup = "$MshPath.bak"
Copy-Item $MshPath $backup -Force
Write-Host ''
Write-Step "Đã lưu bản cũ: $backup"

# Chỉ đổi đúng dòng MeshServer=. Các dòng khác (MeshID, ServerID, MeshName)
# giữ nguyên từng ký tự.
$lines = Get-Content $MshPath
$updated = $lines | ForEach-Object {
    if ($_ -match '^MeshServer=') { "MeshServer=$targetUrl" } else { $_ }
}
Set-Content -Path $MshPath -Value $updated -Encoding ASCII

Write-Step "Đã ghi: MeshServer=$targetUrl"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Restart-Service -Name $ServiceName -Force
    Write-Step 'Đã khởi động lại Mesh Agent'
} else {
    Write-Warn "Không thấy service '$ServiceName' — khởi động lại agent thủ công."
}

Write-Host ''
Write-Good 'Xong. Kiểm tra máy đã hiện trong MeshCentral chưa.'
Write-Host "  Đổi mạng (vào/rời tailnet) thì chạy lại script này.`n"
