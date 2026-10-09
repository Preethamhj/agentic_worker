import logging
import sys
from typing import Any

import structlog


def configure_logging() -> None:
    structlog.configure(
        processors=[
            structlog.contextvars.merge_contextvars,
            structlog.processors.add_log_level,
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.JSONRenderer(),
        ],
        wrapper_class=structlog.make_filtering_bound_logger(logging.INFO),
        context_class=dict,
        logger_factory=structlog.PrintLoggerFactory(sys.stdout),
        cache_logger_on_first_use=True,
    )


def get_logger(
    trace_id: str | None = None,
    task_id: str | None = None,
    step_id: str | None = None,
) -> Any:
    logger = structlog.get_logger()
    context: dict[str, str] = {}
    if trace_id:
        context["trace_id"] = trace_id
    if task_id:
        context["task_id"] = task_id
    if step_id:
        context["step_id"] = step_id
    return logger.bind(**context)
