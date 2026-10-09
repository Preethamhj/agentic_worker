import uuid


def generate_trace_id() -> str:
    return f"trace-{uuid.uuid4().hex[:12]}"


def generate_session_id() -> str:
    return f"sess-{uuid.uuid4().hex[:12]}"


def generate_task_id() -> str:
    return f"task-{uuid.uuid4().hex[:12]}"


def generate_step_id(order: int = 1) -> str:
    return f"step-{order:03d}"


def generate_intent_id() -> str:
    return f"intent-{uuid.uuid4().hex[:12]}"
