import re
import unicodedata


def normalize_text(text: str) -> str:
    """Normalize text: trim, collapse whitespace, Unicode NFC, preserving raw text."""
    if not text:
        return ""
    # NFC normalization
    normalized = unicodedata.normalize("NFC", text)
    # Collapse multiple whitespace characters into a single space
    collapsed = re.sub(r"\s+", " ", normalized.strip())
    return collapsed
