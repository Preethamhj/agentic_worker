import socket
from typing import Any

_orig_connect = socket.socket.connect


def _guarded_connect(self: socket.socket, address: Any) -> Any:
    host = ""
    if isinstance(address, tuple) and len(address) > 0:
        host = str(address[0])
    elif isinstance(address, str):
        host = address

    if host not in ("127.0.0.1", "localhost", "::1", "localhost.localdomain"):
        raise PermissionError(
            f"Outbound non-loopback network connection to '{host}' is strictly forbidden by Jarvis security policy."
        )

    return _orig_connect(self, address)


def install_network_guard() -> None:
    socket.socket.connect = _guarded_connect  # type: ignore[method-assign,assignment]
