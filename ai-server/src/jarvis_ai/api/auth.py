import hmac
import os

from fastapi import Header, HTTPException, status


def get_jarvis_token() -> str:
    token = os.getenv("JARVIS_TOKEN")
    if not token:
        raise RuntimeError("JARVIS_TOKEN environment variable is not set. Refusing server startup.")
    return token


def verify_jarvis_token(x_jarvis_token: str | None = Header(None, alias="X-Jarvis-Token")) -> str:
    expected_token = os.getenv("JARVIS_TOKEN")
    if not expected_token:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="JARVIS_TOKEN environment variable is not configured on server",
        )
    if not x_jarvis_token or not hmac.compare_digest(x_jarvis_token.encode("utf-8"), expected_token.encode("utf-8")):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing X-Jarvis-Token header",
        )
    return x_jarvis_token
