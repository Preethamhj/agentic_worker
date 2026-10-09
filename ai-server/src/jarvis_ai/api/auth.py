import hmac
import os

from fastapi import Header, HTTPException, status


def get_jarvis_token() -> str:
    token = os.getenv("JARVIS_TOKEN")
    if not token:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="JARVIS_TOKEN environment variable is not configured on server",
        )
    return token


def verify_jarvis_token(x_jarvis_token: str = Header(..., alias="X-Jarvis-Token")) -> str:
    expected_token = get_jarvis_token()
    if not hmac.compare_digest(x_jarvis_token.encode("utf-8"), expected_token.encode("utf-8")):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing X-Jarvis-Token header",
        )
    return x_jarvis_token
