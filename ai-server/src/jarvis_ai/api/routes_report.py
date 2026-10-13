from fastapi import APIRouter, Depends, Header

from jarvis_ai.api.auth import verify_jarvis_token
from jarvis_ai.memory.session_store import SessionStore
from jarvis_ai.response.generator import ResponseGenerator
from jarvis_ai.schemas.models import ReportRequestModel, ReportResponseModel

router = APIRouter(prefix="/v1", tags=["Report"])

response_generator = ResponseGenerator()
session_store = SessionStore()
_idempotency_cache: dict[str, ReportResponseModel] = {}


@router.post("/report", response_model=ReportResponseModel, dependencies=[Depends(verify_jarvis_token)])
def report_endpoint(
    request: ReportRequestModel,
    idempotency_key: str | None = Header(None, alias="Idempotency-Key"),
) -> ReportResponseModel:
    if idempotency_key and idempotency_key in _idempotency_cache:
        return _idempotency_cache[idempotency_key]

    response = response_generator.generate_report_response(request)
    session_store.add_task_summary(request.session_id, request.task_id, response.message)

    if idempotency_key:
        _idempotency_cache[idempotency_key] = response

    return response
