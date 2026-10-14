from fastapi import APIRouter, Depends, Header

from jarvis_ai.api.auth import verify_jarvis_token
from jarvis_ai.memory.session_store import SessionStore
from jarvis_ai.response.generator import ResponseGenerator
from jarvis_ai.schemas.models import ReportRequestModel, ReportResponseModel

router = APIRouter(prefix="/v1", tags=["Report"])

response_generator = ResponseGenerator()
session_store = SessionStore()


@router.post("/report", response_model=ReportResponseModel, dependencies=[Depends(verify_jarvis_token)])
def report_endpoint(
    request: ReportRequestModel,
    idempotency_key: str | None = Header(None, alias="Idempotency-Key"),
) -> ReportResponseModel:
    if idempotency_key:
        cached_response = session_store.get_report_response(idempotency_key)
        if cached_response is not None:
            return cached_response

    response = response_generator.generate_report_response(request)

    if idempotency_key:
        return session_store.add_report_response_if_absent(
            idempotency_key,
            request.session_id,
            request.task_id,
            response.message,
            response,
        )

    session_store.add_task_summary(request.session_id, request.task_id, response.message)
    return response
