import socket
from collections.abc import Generator

import pytest

from jarvis_ai.core.config import Settings, is_loopback
from jarvis_ai.core.network_guard import install_network_guard


@pytest.fixture(autouse=True)
def restore_socket_connect() -> Generator[None, None, None]:
    orig_connect = socket.socket.connect
    yield
    socket.socket.connect = orig_connect  # type: ignore[method-assign,assignment]


def test_network_guard_blocks_non_loopback_socket() -> None:
    install_network_guard()

    # Loopback IPv4 address should attempt connection (socket error, not PermissionError)
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        sock.connect(("127.0.0.1", 12345))
    except (ConnectionRefusedError, OSError):
        pass
    finally:
        sock.close()

    # Loopback IPv6 address should attempt connection
    sock6 = socket.socket(socket.AF_INET6, socket.SOCK_STREAM)
    try:
        sock6.connect(("::1", 12345))
    except (ConnectionRefusedError, OSError):
        pass
    finally:
        sock6.close()

    # Non-loopback address must raise PermissionError
    sock_remote = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    with pytest.raises(PermissionError, match="strictly forbidden"):
        sock_remote.connect(("8.8.8.8", 80))
    sock_remote.close()


def test_is_loopback_helper() -> None:
    assert is_loopback("127.0.0.1") is True
    assert is_loopback("localhost") is True
    assert is_loopback("::1") is True
    assert is_loopback("http://127.0.0.1:11434") is True
    assert is_loopback("http://[::1]:11434") is True

    assert is_loopback("8.8.8.8") is False
    assert is_loopback("192.168.1.1") is False
    assert is_loopback("http://example.com:80") is False


def test_settings_rejects_non_loopback_hosts(tmp_path: pytest.TempPathFactory) -> None:
    invalid_server_cfg = tmp_path / "invalid_server.json"  # type: ignore
    invalid_server_cfg.write_text(
        '{"schema_version": 1, "ai_server_host": "192.168.1.100", "ollama_host": "http://127.0.0.1:11434"}'
    )
    with pytest.raises(ValueError, match="ai_server_host.*forbidden"):
        Settings(settings_path=str(invalid_server_cfg))

    invalid_ollama_cfg = tmp_path / "invalid_ollama.json"  # type: ignore
    invalid_ollama_cfg.write_text(
        '{"schema_version": 1, "ai_server_host": "127.0.0.1", "ollama_host": "http://8.8.8.8:11434"}'
    )
    with pytest.raises(ValueError, match="ollama_host.*forbidden"):
        Settings(settings_path=str(invalid_ollama_cfg))
