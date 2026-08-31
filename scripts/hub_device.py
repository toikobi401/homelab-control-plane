#!/usr/bin/env python3
"""Công cụ dòng lệnh quản lý thiết bị trong Personal Device Hub.

Đăng ký máy hiện tại vào hub, xem danh sách, duyệt, thu hồi duyệt, và gỡ hẳn
khỏi sổ đăng ký.

Xem CONTEXT.md §5a (sổ đăng ký thiết bị) và §6 (xác thực).

Ví dụ:
    python hub_device.py register --hub https://100.100.100.100:7189
    python hub_device.py list
    python hub_device.py approve --hostname LAPTOP-ABC
    python hub_device.py revoke  --hostname LAPTOP-ABC     # thu hồi duyệt, đảo ngược được
    python hub_device.py unregister --hostname LAPTOP-ABC  # gỡ hẳn khỏi sổ

Chạy agent để máy NHẬN được lệnh điều khiển (đăng ký thôi là chưa đủ):
    python hub_device.py agent --hub https://100.100.100.100:7189

Chỉ dùng thư viện chuẩn — máy mới chỉ cần Python, không phải pip install gì.
"""

from __future__ import annotations

import argparse
import ctypes
import getpass
import hmac
import http.server
import ipaddress
import json
import os
import platform
import socket
import ssl
import subprocess
import sys
import threading
import urllib.error
import urllib.request
import uuid
from http.cookiejar import CookieJar
from typing import Any

# Dải CGNAT Tailscale dùng. Xem CONTEXT.md §4.
TAILNET_NETWORK = ipaddress.ip_network("100.64.0.0/10")

DEFAULT_HUB_PORT = 7189


class HubError(Exception):
    """Lỗi khi làm việc với hub — in gọn cho người dùng, không phải traceback."""


# ───────────────────────────── Dò địa chỉ ─────────────────────────────


def find_tailnet_address() -> str | None:
    """Địa chỉ tailnet của máy này.

    Ưu tiên hỏi thẳng `tailscale ip`: đó là nguồn chính xác nhất. Nếu không có
    lệnh đó (chưa cài, hoặc không nằm trong PATH) thì rơi về quét card mạng.

    KHÔNG chỉ quét theo dải 100.64.0.0/10: VPN khác (Radmin, một số CGNAT của
    ISP) cũng cấp địa chỉ trong dải này, khớp theo dải thôi thì có ngày trỏ nhầm
    sang mạng khác. Vì vậy nguồn ưu tiên luôn là chính Tailscale.
    """
    address = _tailscale_ip()
    if address:
        return address

    return _scan_tailnet_interface()


def _tailscale_ip() -> str | None:
    """Hỏi chính Tailscale. Trả None nếu chưa cài hoặc chưa đăng nhập."""
    candidates = ["tailscale"]

    if platform.system() == "Windows":
        # Trình cài Windows không luôn thêm vào PATH.
        candidates.append(r"C:\Program Files\Tailscale\tailscale.exe")

    for command in candidates:
        try:
            result = subprocess.run(
                [command, "ip", "-4"],
                capture_output=True,
                text=True,
                timeout=10,
                check=False,
            )
        except (FileNotFoundError, OSError, subprocess.TimeoutExpired):
            continue

        if result.returncode != 0:
            continue

        for line in result.stdout.splitlines():
            candidate = line.strip()
            if _is_tailnet(candidate):
                return candidate

    return None


def _scan_tailnet_interface() -> str | None:
    """Phương án dự phòng: tìm địa chỉ trong dải tailnet trên các card mạng."""
    for address in _local_addresses():
        if _is_tailnet(address):
            return address

    return None


def _local_addresses() -> list[str]:
    """Mọi địa chỉ IPv4 của máy này."""
    addresses: list[str] = []

    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            address = info[4][0]
            if address not in addresses:
                addresses.append(address)
    except socket.gaierror:
        pass

    return addresses


def _is_tailnet(address: str) -> bool:
    try:
        return ipaddress.ip_address(address) in TAILNET_NETWORK
    except ValueError:
        return False


def find_lan_label() -> str | None:
    """Nhãn LAN suy từ subnet của card mạng vật lý (§5a.1).

    Máy cùng nhãn thì đánh thức được cho nhau — magic packet là broadcast tầng 2,
    không qua router.

    Phải loại card ẢO, không chỉ loại dải tailnet: máy có VirtualBox/VMware/WSL
    thì các card đó cũng mang địa chỉ riêng tư (192.168.56.x là mặc định của
    VirtualBox). Lấy nhầm chúng làm nhãn LAN là kết luận sai máy nào đánh thức
    được máy nào — đã gặp thật trên máy phát triển.
    """
    for address, is_physical in _local_ipv4_details():
        if not is_physical or _is_tailnet(address):
            continue

        try:
            parsed = ipaddress.ip_address(address)
        except ValueError:
            continue

        if not parsed.is_private or parsed.is_loopback or parsed.is_link_local:
            continue

        # Giả định /24 — đúng với gần như mọi mạng gia đình. Agent .NET đọc
        # được prefix thật; script này chỉ cần đủ để nhóm máy cùng mạng.
        return str(ipaddress.ip_network(f"{address}/24", strict=False))

    return None


# Tên card mạng của phần mềm ảo hoá và VPN — không phải LAN vật lý.
VIRTUAL_ADAPTER_HINTS = (
    "virtualbox",
    "vmware",
    "hyper-v",
    "vethernet",
    "wsl",
    "docker",
    "radmin",
    "tailscale",
    "zerotier",
    "loopback",
    "tap-",
    "tun",
)


def _local_ipv4_details() -> list[tuple[str, bool]]:
    """Địa chỉ IPv4 kèm cờ "có phải card vật lý không".

    Trên Windows hỏi PowerShell để biết tên card. Nền tảng khác (hoặc khi
    PowerShell hỏng) rơi về danh sách địa chỉ trần và coi tất cả là vật lý —
    thà đoán rộng còn hơn bỏ sót LAN thật.
    """
    if platform.system() == "Windows":
        detailed = _windows_ipv4_details()
        if detailed:
            return detailed

    return [(address, True) for address in _local_addresses()]


def _windows_ipv4_details() -> list[tuple[str, bool]]:
    script = (
        "Get-NetIPAddress -AddressFamily IPv4 | "
        "Select-Object IPAddress,InterfaceAlias | ConvertTo-Json -Compress"
    )

    try:
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", script],
            capture_output=True,
            text=True,
            timeout=15,
            check=False,
        )
    except (FileNotFoundError, OSError, subprocess.TimeoutExpired):
        return []

    if result.returncode != 0 or not result.stdout.strip():
        return []

    try:
        payload = json.loads(result.stdout)
    except ValueError:
        return []

    if isinstance(payload, dict):
        payload = [payload]

    details: list[tuple[str, bool]] = []
    for entry in payload:
        address = entry.get("IPAddress", "")
        alias = (entry.get("InterfaceAlias") or "").lower()

        if not address or address.startswith("127."):
            continue

        is_physical = not any(hint in alias for hint in VIRTUAL_ADAPTER_HINTS)
        details.append((address, is_physical))

    return details


def find_mac_address() -> str | None:
    """MAC của máy này, dạng AA:BB:CC:DD:EE:FF.

    §5a: MAC bắt buộc để đánh thức, và phải ghi lại lúc máy còn online — lúc đã
    tắt thì không hỏi được nữa.
    """
    node = uuid.getnode()

    # getnode() sinh số ngẫu nhiên khi không đọc được MAC thật; bit multicast
    # được bật để báo điều đó. Giá trị ngẫu nhiên thì vô dụng cho việc đánh thức.
    if (node >> 40) & 0x01:
        return None

    return ":".join(f"{(node >> shift) & 0xFF:02X}" for shift in range(40, -1, -8))


# ───────────────────────────── Gọi hub ─────────────────────────────


class HubClient:
    """Client HTTP cho hub. Giữ cookie phiên và CSRF token giữa các lời gọi."""

    def __init__(self, base_url: str, insecure: bool = False) -> None:
        self.base_url = base_url.rstrip("/")
        self._csrf_token: str | None = None
        self._csrf_header = "X-CSRF-Token"

        context = ssl.create_default_context()
        if insecure:
            # Chứng chỉ dev tự ký lúc phát triển. Khi có `tailscale cert` thì bỏ
            # cờ này — đừng để nó thành mặc định.
            context.check_hostname = False
            context.verify_mode = ssl.CERT_NONE

        self._opener = urllib.request.build_opener(
            urllib.request.HTTPSHandler(context=context),
            urllib.request.HTTPCookieProcessor(CookieJar()),
        )

    def request(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
        bearer: str | None = None,
    ) -> Any:
        """Gọi API. Tự đính CSRF token cho request đổi trạng thái."""
        if method != "GET" and bearer is None:
            self._ensure_csrf_token()

        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(f"{self.base_url}{path}", data=data, method=method)

        if data is not None:
            request.add_header("Content-Type", "application/json")

        if bearer is not None:
            # Agent xác thực bằng khoá chung, không dùng cookie phiên (§6.4).
            request.add_header("Authorization", f"Bearer {bearer}")
        elif method != "GET" and self._csrf_token:
            request.add_header(self._csrf_header, self._csrf_token)

        try:
            with self._opener.open(request, timeout=20) as response:
                payload = response.read()
                return json.loads(payload) if payload else None
        except urllib.error.HTTPError as error:
            raise HubError(self._describe(error)) from error
        except urllib.error.URLError as error:
            reason = str(error.reason)

            # Lỗi chứng chỉ thường do kho CA của Python cũ, không phải server
            # hỏng — nói thẳng cách sửa thay vì để người dùng tự đoán.
            if "CERTIFICATE_VERIFY_FAILED" in reason:
                raise HubError(
                    f"Không xác minh được chứng chỉ của {self.base_url}:\n"
                    f"  {reason}\n"
                    "Thường do kho CA của Python cũ, chưa tin root mới của Let's Encrypt.\n"
                    "Cách sửa, theo thứ tự ưu tiên:\n"
                    "  1. Nâng cấp Python lên bản mới hơn\n"
                    "  2. pip install certifi   (script tự dùng nếu có)\n"
                    "  3. Thêm --insecure       (bỏ xác minh — chỉ dùng tạm)"
                ) from error

            raise HubError(
                f"Không kết nối được hub tại {self.base_url}: {reason}.\n"
                "Kiểm tra backend còn chạy không, và Tailscale còn kết nối không."
            ) from error

    def login(self, password: str) -> None:
        self.request("POST", "/api/auth/login", {"password": password})

        # Đăng nhập xoay session nên token cũ mất hiệu lực — lấy lại.
        self._csrf_token = None

    def _ensure_csrf_token(self) -> None:
        if self._csrf_token:
            return

        payload = self.request("GET", "/api/antiforgery/token")
        if isinstance(payload, dict):
            self._csrf_token = payload.get("token")
            self._csrf_header = payload.get("headerName") or self._csrf_header

    @staticmethod
    def _describe(error: urllib.error.HTTPError) -> str:
        """Thông báo đọc được, kèm gợi ý theo mã lỗi."""
        detail = ""
        try:
            body = json.loads(error.read())
            if isinstance(body, dict):
                detail = body.get("title") or body.get("detail") or ""
        except (ValueError, OSError):
            pass

        hints = {
            401: "Sai mật khẩu hub, hoặc sai khoá chung (Agent:SharedSecret).",
            403: "Không đủ quyền cho thao tác này.",
            404: "Không tìm thấy — kiểm tra lại địa chỉ hub.",
            503: "Hub chưa cấu hình khoá chung với agent. Xem docs/agent-setup.md.",
        }

        parts = [f"HTTP {error.code}"]
        if detail:
            parts.append(detail)
        if error.code in hints:
            parts.append(hints[error.code])

        return " — ".join(parts)


# ───────────────────────────── Lệnh ─────────────────────────────


def resolve_hub_url(argument: str | None) -> str:
    """Địa chỉ hub: tham số dòng lệnh, biến môi trường, rồi mới hỏi."""
    url = argument or os.environ.get("HUB_URL")

    if not url:
        raise HubError(
            "Chưa biết địa chỉ hub. Truyền --hub https://<ip-tailnet>:7189 "
            "hoặc đặt biến môi trường HUB_URL."
        )

    if "://" not in url:
        url = f"https://{url}"

    # Thiếu cổng thì thêm cổng mặc định — dễ gõ nhầm chỗ này.
    if url.count(":") == 1:
        url = f"{url}:{DEFAULT_HUB_PORT}"

    return url


def resolve_secret(argument: str | None) -> str:
    """Khoá chung: tham số, biến môi trường, rồi mới hỏi (không hiện lúc gõ)."""
    secret = argument or os.environ.get("HUB_AGENT_SECRET")

    if not secret:
        secret = getpass.getpass("Khoá chung với agent (Agent:SharedSecret): ")

    if not secret:
        raise HubError("Chưa có khoá chung — không đăng ký được.")

    return secret


def resolve_password(argument: str | None) -> str:
    password = argument or os.environ.get("HUB_PASSWORD")

    if not password:
        password = getpass.getpass("Mật khẩu hub: ")

    if not password:
        raise HubError("Chưa có mật khẩu — không đăng nhập được.")

    return password


def command_register(args: argparse.Namespace) -> int:
    """Đăng ký máy hiện tại vào hub."""
    client = HubClient(resolve_hub_url(args.hub), insecure=args.insecure)
    secret = resolve_secret(args.secret)

    hostname = args.hostname or socket.gethostname()
    tailnet = args.tailnet_address or find_tailnet_address()
    mac = args.mac or find_mac_address()
    lan_label = args.lan_label or find_lan_label()

    print(f"Máy      : {hostname}")
    print(f"Hệ điều hành: {_os_name()}")
    print(f"Tailnet  : {tailnet or '(không dò được)'}")
    print(f"MAC      : {mac or '(không đọc được)'}")
    print(f"Nhãn LAN : {lan_label or '(không dò được)'}")
    print(f"Máy chạy hub: {'có' if args.backend_host else 'không'}")

    if tailnet is None:
        # Không chặn: hub lấy địa chỉ từ chính kết nối, nên vẫn đăng ký được.
        # Nhưng nói rõ để người dùng biết Tailscale có thể chưa chạy.
        print(
            "\nCảnh báo: không dò được địa chỉ tailnet. Hub sẽ lấy địa chỉ từ "
            "kết nối. Kiểm tra `tailscale status` nếu máy này cần nhận lệnh.",
            file=sys.stderr,
        )

    if mac is None:
        print(
            "Cảnh báo: không đọc được MAC. Máy này sẽ KHÔNG đánh thức được từ "
            "xa (§5a.1). Truyền --mac nếu biết giá trị đúng.",
            file=sys.stderr,
        )

    if args.dry_run:
        print("\n--dry-run: dừng ở đây, chưa gửi gì lên hub.")
        return 0

    device = client.request(
        "POST",
        "/api/devices/register",
        {
            "hostname": hostname,
            "operatingSystem": _os_name(),
            "macAddress": mac,
            "lanLabel": lan_label,
            "isBackendHost": args.backend_host,
        },
        bearer=secret,
    )

    print(f"\nĐã đăng ký. Id: {device['id']}")

    if not device["isApproved"]:
        # §5a: thiết bị mới phải được duyệt thủ công trước khi nhận lệnh.
        print(
            "Trạng thái: CHỜ DUYỆT — chưa nhận được lệnh điều khiển.\n"
            f"Duyệt bằng giao diện web, hoặc: "
            f"python {os.path.basename(__file__)} approve --hostname {hostname}"
        )
    else:
        print("Trạng thái: đã duyệt.")

    return 0


def command_list(args: argparse.Namespace) -> int:
    client = _authenticated_client(args)
    devices = client.request("GET", "/api/devices/registered")

    if not devices:
        print("Chưa có thiết bị nào đăng ký.")
        return 0

    header = f"{'TÊN MÁY':<24} {'TRẠNG THÁI':<12} {'TAILNET':<17} {'MAC':<18} NHÃN LAN"
    print(header)
    print("-" * len(header))

    for device in devices:
        status = "đã duyệt" if device["isApproved"] else "CHỜ DUYỆT"
        if device["isBackendHost"]:
            status += "*"

        print(
            f"{device['hostname']:<24} {status:<12} "
            f"{device['tailnetAddress'] or '-':<17} "
            f"{device['macAddress'] or '-':<18} "
            f"{device['lanLabel'] or '-'}"
        )

    print("\n* = máy đang chạy hub (không tắt/khởi động lại được từ giao diện — §5a điều 5)")
    return 0


def command_approve(args: argparse.Namespace) -> int:
    client = _authenticated_client(args)
    device = _find_device(client, args)

    client.request("POST", f"/api/devices/{device['id']}/approve")
    print(f"Đã duyệt {device['hostname']} — máy này giờ nhận được lệnh điều khiển.")
    return 0


def command_revoke(args: argparse.Namespace) -> int:
    """Thu hồi duyệt. Thiết bị vẫn trong sổ, chỉ là không nhận lệnh nữa."""
    client = _authenticated_client(args)
    device = _find_device(client, args)

    if not _confirm(
        args,
        f"Thu hồi duyệt {device['hostname']}? Máy này sẽ không nhận được lệnh nữa.",
    ):
        return 1

    client.request("POST", f"/api/devices/{device['id']}/revoke-approval")
    print(f"Đã thu hồi duyệt {device['hostname']}. Duyệt lại được bất cứ lúc nào.")
    return 0


def command_unregister(args: argparse.Namespace) -> int:
    """Gỡ hẳn thiết bị khỏi sổ đăng ký."""
    client = _authenticated_client(args)
    device = _find_device(client, args)

    if not _confirm(
        args,
        f"GỠ HẲN {device['hostname']} khỏi sổ đăng ký? "
        "Máy này phải đăng ký lại và chờ duyệt lại từ đầu.",
    ):
        return 1

    client.request("DELETE", f"/api/devices/{device['id']}")
    print(f"Đã gỡ {device['hostname']} khỏi sổ. Nhật ký kiểm toán cũ vẫn giữ nguyên.")
    return 0


def command_detect(args: argparse.Namespace) -> int:
    """In những gì script dò được, không gọi hub. Dùng để chẩn đoán."""
    print(f"Tên máy      : {socket.gethostname()}")
    print(f"Hệ điều hành : {_os_name()}")
    print(f"Địa chỉ tailnet: {find_tailnet_address() or '(không dò được)'}")
    print(f"MAC          : {find_mac_address() or '(không đọc được)'}")
    print(f"Nhãn LAN     : {find_lan_label() or '(không dò được)'}")
    print(f"Mọi IPv4     : {', '.join(_local_addresses()) or '(không có)'}")
    return 0


# ───────────────────────────── Chế độ agent ─────────────────────────────
#
# CONTEXT.md §3 nói agent dùng "cùng codebase .NET". Bản Python này là ngoại lệ
# có chủ đích cho máy chưa cài .NET — xem nợ kỹ thuật trong PROGRESS.md. Hai bản
# phải giữ cùng hành vi: cùng đường dẫn, cùng cách xác thực, cùng tập hành động.


# §5a: tập hành động ĐÓNG. Không có "chạy lệnh tuỳ ý", và đừng thêm.
POWER_ACTIONS = ("shutdown", "restart", "sleep", "lock")


def _execute_power_action(action: str) -> tuple[bool, str]:
    """Thực thi lệnh điều khiển nguồn. Trả (thành công, thông báo).

    §5a điều 3: KHÔNG gọi qua shell. Dùng API Windows trực tiếp, hoặc
    subprocess với danh sách tham số — không bao giờ ghép chuỗi. Không tham số
    nào ở đây đến từ input người dùng, và cách viết này đảm bảo giữ nguyên vậy.
    """
    if platform.system() != "Windows":
        return False, f"Chưa hỗ trợ {platform.system()}"

    try:
        if action == "shutdown":
            return _run_shutdown_tool("/s", "/t", "0")

        if action == "restart":
            return _run_shutdown_tool("/r", "/t", "0")

        if action == "lock":
            ok = bool(ctypes.windll.user32.LockWorkStation())
            return ok, "Đã khoá màn hình" if ok else "Không khoá được màn hình"

        if action == "sleep":
            # hibernate=False -> sleep. Tham số cuối False để máy vẫn đánh thức
            # được bằng magic packet (§5a.1).
            ok = bool(ctypes.windll.powrprof.SetSuspendState(False, False, False))
            return ok, "Đã chuyển sang chế độ ngủ" if ok else "Không chuyển được sang ngủ"

        return False, f"Hành động không hỗ trợ: {action}"
    except OSError as error:
        return False, f"Lỗi hệ thống: {error}"


def _run_shutdown_tool(*arguments: str) -> tuple[bool, str]:
    """Gọi shutdown.exe với danh sách tham số, không qua shell."""
    result = subprocess.run(
        ["shutdown.exe", *arguments],
        capture_output=True,
        text=True,
        timeout=15,
        check=False,
    )

    if result.returncode == 0:
        return True, "Đã gửi lệnh"

    return False, f"shutdown.exe trả mã {result.returncode}"


class _AgentHandler(http.server.BaseHTTPRequestHandler):
    """Xử lý request từ hub. Khớp đường dẫn với agent .NET."""

    shared_secret = ""

    def do_GET(self) -> None:  # noqa: N802 — tên do BaseHTTPRequestHandler quy định
        if self.path == "/agent/health":
            self._respond(200, {"status": "ok"})
        else:
            self._respond(404, {"title": "Không tìm thấy"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/agent/power":
            self._respond(404, {"title": "Không tìm thấy"})
            return

        if not self._is_authorized():
            # §6.4: tailnet là lớp phòng thủ thứ nhất, không phải duy nhất.
            # KHÔNG bao giờ tin "gọi từ 100.x nên bỏ qua xác thực".
            print(f"[agent] Từ chối lệnh: khoá chung sai (từ {self.client_address[0]})")
            self._respond(401, {"title": "Khoá chung không hợp lệ"})
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length) or b"{}")
        except (ValueError, OSError):
            self._respond(400, {"title": "Body không hợp lệ"})
            return

        # Khớp key KHÔNG phân biệt hoa thường: backend .NET serialize record
        # thành {"Action": "Lock"}, còn client khác có thể gửi {"action": ...}.
        # Bản .NET nhận cả hai nhờ JSON binding mặc định — bản này phải theo.
        action = ""
        for key, value in payload.items():
            if key.lower() == "action":
                action = str(value).lower()
                break

        # Chỉ nhận đúng các giá trị trong tập đóng. Chuỗi lạ bị từ chối ngay,
        # không có nhánh mặc định nào chạy thứ gì đó.
        if action not in POWER_ACTIONS:
            print(f"[agent] Hành động không hợp lệ: {action!r}")
            self._respond(400, {"title": "Hành động không hợp lệ"})
            return

        print(f"[agent] Thực thi {action}")
        succeeded, message = _execute_power_action(action)

        if succeeded:
            self._respond(204, None)
        else:
            print(f"[agent] Thất bại: {message}")
            self._respond(500, {"title": message})

    def _is_authorized(self) -> bool:
        """So sánh khoá theo thời gian cố định — tránh lộ qua thời gian phản hồi."""
        header = self.headers.get("Authorization", "")

        if not header.startswith("Bearer "):
            return False

        return hmac.compare_digest(header[len("Bearer ") :], self.shared_secret)

    def _respond(self, status: int, body: dict[str, Any] | None) -> None:
        data = json.dumps(body).encode() if body is not None else b""
        self.send_response(status)

        if data:
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))

        self.end_headers()

        if data:
            self.wfile.write(data)

    def log_message(self, format: str, *args: Any) -> None:
        """Tắt log mặc định — nó ồn và ghi cả đường dẫn (§6.5 mục 4)."""


def command_agent(args: argparse.Namespace) -> int:
    """Chạy agent: nhận lệnh điều khiển và báo danh định kỳ."""
    hub_url = resolve_hub_url(args.hub)
    secret = resolve_secret(args.secret)

    if platform.system() != "Windows":
        print(
            f"Cảnh báo: {platform.system()} chưa được hỗ trợ — agent sẽ báo danh "
            "nhưng không thực thi được lệnh điều khiển nguồn.",
            file=sys.stderr,
        )

    _AgentHandler.shared_secret = secret

    # Bind mọi địa chỉ để hub gọi tới được; ranh giới bảo vệ là khoá chung ở
    # trên, không phải địa chỉ (§6.4).
    server = http.server.ThreadingHTTPServer(("", args.port), _AgentHandler)

    stop = threading.Event()
    heartbeat = threading.Thread(
        target=_heartbeat_loop,
        args=(hub_url, secret, args, stop),
        daemon=True,
    )
    heartbeat.start()

    print(f"Agent đang chạy trên cổng {args.port}. Hub: {hub_url}")
    print("Ctrl+C để dừng.")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nĐang dừng agent...")
    finally:
        stop.set()
        server.shutdown()

    return 0


def _heartbeat_loop(
    hub_url: str,
    secret: str,
    args: argparse.Namespace,
    stop: threading.Event,
) -> None:
    """Báo danh với hub, lặp lại định kỳ.

    Hub tắt là chuyện bình thường — ghi cảnh báo rồi thử lại, không để agent
    chết theo.
    """
    client = HubClient(hub_url, insecure=args.insecure)
    first = True

    while not stop.is_set():
        try:
            device = client.request(
                "POST",
                "/api/devices/register",
                {
                    "hostname": args.hostname or socket.gethostname(),
                    "operatingSystem": _os_name(),
                    "macAddress": args.mac or find_mac_address(),
                    "lanLabel": args.lan_label or find_lan_label(),
                    "isBackendHost": args.backend_host,
                    # Phân biệt với đăng ký bằng script: hub cần biết agent đã
                    # thật sự chạy để báo lỗi đúng khi máy im lặng.
                    "fromAgent": True,
                },
                bearer=secret,
            )

            if first:
                status = "đã duyệt" if device["isApproved"] else "CHỜ DUYỆT"
                print(f"Đã báo danh với hub. Trạng thái: {status}")

                if not device["isApproved"]:
                    print(
                        "  Máy này chưa nhận được lệnh cho tới khi được duyệt (§5a). "
                        "Duyệt qua giao diện web."
                    )

                first = False
        except HubError as error:
            print(f"[agent] Không báo danh được: {error}", file=sys.stderr)

        stop.wait(args.heartbeat)


# ───────────────────────────── Tiện ích ─────────────────────────────


def _os_name() -> str:
    system = platform.system()
    return {"Windows": "windows", "Linux": "linux", "Darwin": "macOS"}.get(system, system.lower())


def _authenticated_client(args: argparse.Namespace) -> HubClient:
    """Client đã đăng nhập — dùng cho thao tác cần quyền người dùng (§6.4)."""
    client = HubClient(resolve_hub_url(args.hub), insecure=args.insecure)
    client.login(resolve_password(args.password))
    return client


def _find_device(client: HubClient, args: argparse.Namespace) -> dict[str, Any]:
    """Tìm thiết bị theo --hostname hoặc --id; mặc định là máy hiện tại."""
    devices = client.request("GET", "/api/devices/registered")

    if args.id:
        for device in devices:
            if device["id"] == args.id:
                return device
        raise HubError(f"Không tìm thấy thiết bị có id {args.id}.")

    hostname = args.hostname or socket.gethostname()
    matches = [d for d in devices if d["hostname"].lower() == hostname.lower()]

    if not matches:
        known = ", ".join(d["hostname"] for d in devices) or "(sổ đăng ký trống)"
        raise HubError(f"Không tìm thấy máy tên {hostname}. Đang có: {known}")

    if len(matches) > 1:
        raise HubError(f"Có nhiều máy tên {hostname} — dùng --id để chỉ đích danh.")

    return matches[0]


def _confirm(args: argparse.Namespace, question: str) -> bool:
    """Hỏi lại trước thao tác không đảo ngược dễ dàng (§5a điều 4)."""
    if args.yes:
        return True

    if not sys.stdin.isatty():
        print(
            "Cần xác nhận nhưng không có terminal. Thêm --yes nếu chắc chắn.",
            file=sys.stderr,
        )
        return False

    answer = input(f"{question} [y/N] ").strip().lower()
    return answer in ("y", "yes")


def _add_common_arguments(parser: argparse.ArgumentParser) -> None:
    """Cờ dùng chung.

    Thêm vào CẢ parser gốc lẫn từng lệnh con để `--hub` đặt trước hay sau lệnh
    đều chạy. argparse mặc định chỉ nhận cờ toàn cục TRƯỚC lệnh con — một cái
    bẫy khó chịu mà chính người viết cũng vấp.
    """
    parser.add_argument(
        "--hub",
        help="Địa chỉ hub, ví dụ https://100.100.100.100:7189 (hoặc đặt HUB_URL).",
    )
    parser.add_argument(
        "--insecure",
        action="store_true",
        default=None,
        help="Bỏ kiểm tra chứng chỉ TLS. Chỉ dùng với chứng chỉ dev tự ký.",
    )
    parser.add_argument(
        "--yes",
        "-y",
        action="store_true",
        default=None,
        help="Không hỏi xác nhận.",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="hub_device.py",
        description="Quản lý thiết bị trong Personal Device Hub.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )

    _add_common_arguments(parser)

    subparsers = parser.add_subparsers(dest="command", required=True)

    register = subparsers.add_parser("register", help="Đăng ký máy hiện tại vào hub")
    _add_common_arguments(register)
    register.add_argument("--secret", help="Khoá chung (hoặc đặt HUB_AGENT_SECRET).")
    register.add_argument("--hostname", help="Ghi đè tên máy tự dò.")
    register.add_argument("--mac", help="Ghi đè MAC tự dò.")
    register.add_argument("--tailnet-address", help="Ghi đè địa chỉ tailnet tự dò.")
    register.add_argument("--lan-label", help="Ghi đè nhãn LAN tự dò.")
    register.add_argument(
        "--backend-host",
        action="store_true",
        help="Máy này đang chạy hub. Bật cờ này thì nó không tự tắt được (§5a điều 5).",
    )
    register.add_argument(
        "--dry-run",
        action="store_true",
        help="Chỉ in thông tin dò được, không gửi lên hub.",
    )
    register.set_defaults(handler=command_register)

    listing = subparsers.add_parser("list", help="Liệt kê thiết bị đã đăng ký")
    _add_common_arguments(listing)
    listing.add_argument("--password", help="Mật khẩu hub (hoặc đặt HUB_PASSWORD).")
    listing.set_defaults(handler=command_list)

    for name, handler, help_text in (
        ("approve", command_approve, "Duyệt thiết bị để nó nhận được lệnh"),
        ("revoke", command_revoke, "Thu hồi duyệt (đảo ngược được)"),
        ("unregister", command_unregister, "Gỡ hẳn thiết bị khỏi sổ đăng ký"),
    ):
        sub = subparsers.add_parser(name, help=help_text)
        _add_common_arguments(sub)
        sub.add_argument("--password", help="Mật khẩu hub (hoặc đặt HUB_PASSWORD).")
        sub.add_argument("--hostname", help="Tên máy. Mặc định là máy hiện tại.")
        sub.add_argument("--id", help="Id thiết bị, dùng khi trùng tên máy.")
        sub.set_defaults(handler=handler)

    detect = subparsers.add_parser("detect", help="In thông tin dò được, không gọi hub")
    _add_common_arguments(detect)
    detect.set_defaults(handler=command_detect)

    agent = subparsers.add_parser(
        "agent",
        help="Chạy agent: nhận lệnh điều khiển và báo danh định kỳ",
    )
    _add_common_arguments(agent)
    agent.add_argument("--secret", help="Khoá chung (hoặc đặt HUB_AGENT_SECRET).")
    agent.add_argument("--hostname", help="Ghi đè tên máy tự dò.")
    agent.add_argument("--mac", help="Ghi đè MAC tự dò.")
    agent.add_argument("--lan-label", help="Ghi đè nhãn LAN tự dò.")
    agent.add_argument(
        "--port",
        type=int,
        default=5199,
        help="Cổng agent lắng nghe. Phải khớp Agent:Port bên hub (mặc định 5199).",
    )
    agent.add_argument(
        "--heartbeat",
        type=int,
        default=60,
        help="Số giây giữa hai lần báo danh (mặc định 60).",
    )
    agent.add_argument(
        "--backend-host",
        action="store_true",
        help="Máy này đang chạy hub. Bật cờ này thì nó không tự tắt được (§5a điều 5).",
    )
    agent.set_defaults(handler=command_agent)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()

    # Cờ chung xuất hiện ở hai chỗ; lệnh con ghi đè parser gốc, và None nghĩa là
    # "không truyền ở đây" nên lấy giá trị của bên kia.
    args = parser.parse_args(argv)

    # Cờ chung khai ở cả parser gốc lẫn lệnh con, nên lần phân tích sau ghi đè
    # lần trước bằng None. Quét lại argv để biết giá trị nào người dùng thật sự
    # gõ — dù họ đặt trước hay sau tên lệnh.
    tokens = list(sys.argv[1:] if argv is None else argv)

    if args.hub is None:
        for index, token in enumerate(tokens):
            if token == "--hub" and index + 1 < len(tokens):
                args.hub = tokens[index + 1]
                break
            if token.startswith("--hub="):
                args.hub = token.split("=", 1)[1]
                break

    args.insecure = bool(args.insecure) or "--insecure" in tokens
    args.yes = bool(args.yes) or "--yes" in tokens or "-y" in tokens

    try:
        return args.handler(args)
    except HubError as error:
        print(f"Lỗi: {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("\nĐã huỷ.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    sys.exit(main())
