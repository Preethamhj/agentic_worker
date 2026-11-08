import datetime
import re

from jarvis_ai.core.ids import generate_intent_id
from jarvis_ai.nlu.normalizer import normalize_text
from jarvis_ai.nlu.risk import detect_keyword_risk
from jarvis_ai.schemas.models import IntentModel, RiskModel


class ReminderTimeError(ValueError):
    """The requested reminder time needs clarification."""


def _absolute_reminder_time(value: str, now: datetime.datetime) -> datetime.datetime:
    match = re.fullmatch(
        r"\s*(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\s*(today|tomorrow)?\s*",
        value,
        re.IGNORECASE,
    )
    if match is None:
        raise ReminderTimeError("Use a local time such as 5:30 pm today or 14:15 tomorrow.")
    hour = int(match.group(1))
    minute = int(match.group(2) or 0)
    meridiem = (match.group(3) or "").lower()
    day = (match.group(4) or "").lower()
    if minute > 59 or (meridiem and not 1 <= hour <= 12) or (not meridiem and hour > 23):
        raise ReminderTimeError("The reminder time is invalid.")
    if meridiem:
        hour = hour % 12 + (12 if meridiem == "pm" else 0)
    local_now = now.astimezone()
    target_date = local_now.date() + datetime.timedelta(days=1 if day == "tomorrow" else 0)
    wall = datetime.datetime.combine(target_date, datetime.time(hour, minute))
    if not day and wall <= local_now.replace(tzinfo=None):
        wall += datetime.timedelta(days=1)

    candidates = [
        wall.replace(fold=fold).astimezone().astimezone(datetime.UTC)
        for fold in (0, 1)
    ]
    valid = [candidate for candidate in candidates if candidate.astimezone().replace(tzinfo=None) == wall]
    if not valid or len(set(valid)) > 1:
        raise ReminderTimeError("That local time is ambiguous or does not exist. Choose another time.")
    if valid[0] <= now:
        raise ReminderTimeError("That reminder time is in the past. Choose a future time.")
    return valid[0]


def parse_rules_fast_path(text: str) -> IntentModel | None:
    norm = normalize_text(text)
    norm_lower = norm.lower()
    intent_id = generate_intent_id()

    # Rule 1: fs.create_file
    m_file = re.search(
        r"create\s+file\s+['\"]?([^'\"]+?)['\"]?\s+with\s+content\s+['\"]?([^'\"]+?)['\"]?$",
        norm,
        re.IGNORECASE,
    )
    if not m_file:
        m_file = re.search(r"write\s+file\s+['\"]?([^'\"]+?)['\"]?\s+content\s+['\"]?([^'\"]+?)['\"]?$", norm, re.IGNORECASE)

    if m_file:
        file_path = m_file.group(1).strip()
        content = m_file.group(2)
        risk_lvl, reasons = detect_keyword_risk(norm)
        return IntentModel(
            intent_id=intent_id,
            kind="command",
            action="fs.create_file",
            confidence=1.0,
            entities={"path": file_path, "content": content},
            risk=RiskModel(level=risk_lvl, reasons=reasons),
            source="rules",
        )

    # Rule 2: fs.create_folder
    m_folder = re.search(
        r"(?:create\s+folder|create\s+directory|make\s+directory)\s+['\"]?([^'\"]+?)['\"]?$",
        norm,
        re.IGNORECASE,
    )
    if m_folder:
        folder_path = m_folder.group(1).strip()
        risk_lvl, reasons = detect_keyword_risk(norm)
        return IntentModel(
            intent_id=intent_id,
            kind="command",
            action="fs.create_folder",
            confidence=1.0,
            entities={"path": folder_path},
            risk=RiskModel(level=risk_lvl, reasons=reasons),
            source="rules",
        )

    # Rule 3: fs.search_files
    m_search = re.search(
        r"(?:search\s+files|find\s+files|list\s+files)\s+['\"]?([^'\"]+?)['\"]?(?:\s+in\s+['\"]?([^'\"]+?)['\"]?)?$",
        norm,
        re.IGNORECASE,
    )
    if m_search:
        pattern = m_search.group(1).strip()
        root_path = m_search.group(2).strip() if m_search.group(2) else ""
        risk_lvl, reasons = detect_keyword_risk(norm)
        return IntentModel(
            intent_id=intent_id,
            kind="query",
            action="fs.search_files",
            confidence=1.0,
            entities={"pattern": pattern, "root_path": root_path},
            risk=RiskModel(level=risk_lvl, reasons=reasons),
            source="rules",
        )

    # Rule 4: app.launch
    m_app = re.search(
        r"(?:open\s+app|launch\s+app|launch|open)\s+(notepad|calc|calculator|mspaint|paint)",
        norm_lower,
    )
    if m_app:
        app_raw = m_app.group(1)
        if app_raw in ("calc", "calculator"):
            app_id = "calc"
        elif app_raw in ("mspaint", "paint"):
            app_id = "mspaint"
        else:
            app_id = "notepad"

        risk_lvl, reasons = detect_keyword_risk(norm)
        return IntentModel(
            intent_id=intent_id,
            kind="command",
            action="app.launch",
            confidence=1.0,
            entities={"app_id": app_id},
            risk=RiskModel(level=risk_lvl, reasons=reasons),
            source="rules",
        )

    # Rule 5: task.schedule_reminder
    m_remind = re.search(
        r"(?:remind\s+me|schedule\s+reminder)\s+['\"]?(.+?)['\"]?\s+"
        r"(?:in\s+(\d{1,3})\s+minutes?|at\s+['\"]?([^'\"]+?)['\"]?)$",
        norm,
        re.IGNORECASE,
    )
    if m_remind:
        msg = m_remind.group(1).strip()
        now = datetime.datetime.now(datetime.UTC)
        minutes = m_remind.group(2)
        if minutes is not None:
            delay = int(minutes)
            if not 1 <= delay <= 1440:
                return None
            scheduled_at = now + datetime.timedelta(minutes=delay)
        else:
            scheduled_at = _absolute_reminder_time(m_remind.group(3), now)
        time_iso = scheduled_at.replace(microsecond=0).isoformat()
        risk_lvl, reasons = detect_keyword_risk(norm)
        return IntentModel(
            intent_id=intent_id,
            kind="command",
            action="task.schedule_reminder",
            confidence=1.0,
            entities={"time_iso": time_iso, "message": msg},
            risk=RiskModel(level=risk_lvl, reasons=reasons),
            source="rules",
        )

    return None
