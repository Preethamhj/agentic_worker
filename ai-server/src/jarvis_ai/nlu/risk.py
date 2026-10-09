from typing import Literal

RISK_HIGH_KEYWORDS = ["delete", "remove", "format", "registry", "powershell", "cmd", "exec", "kill", "shutdown"]
RISK_MEDIUM_KEYWORDS = ["create", "write", "launch", "open", "schedule", "remind", "modify"]


def detect_keyword_risk(text: str) -> tuple[Literal["low", "medium", "high"], list[str]]:
    text_lower = text.lower()
    reasons: list[str] = []

    for kw in RISK_HIGH_KEYWORDS:
        if kw in text_lower:
            reasons.append(f"Keyword hint '{kw}' triggers HIGH risk")
            return "high", reasons

    for kw in RISK_MEDIUM_KEYWORDS:
        if kw in text_lower:
            reasons.append(f"Keyword hint '{kw}' triggers MEDIUM risk")
            return "medium", reasons

    reasons.append("Read-only or safe query triggers LOW risk")
    return "low", reasons


def max_risk(
    risk1: Literal["low", "medium", "high"], risk2: Literal["low", "medium", "high"]
) -> Literal["low", "medium", "high"]:
    rank = {"low": 1, "medium": 2, "high": 3}
    return risk1 if rank[risk1] >= rank[risk2] else risk2
