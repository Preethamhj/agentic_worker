from fastapi import APIRouter, Depends

from jarvis_ai.api.auth import verify_jarvis_token
from jarvis_ai.memory.session_store import SessionStore
from jarvis_ai.response.generator import ResponseGenerator
from jarvis_ai.schemas.models import ReportRequestModel, ReportResponseModel

router = APIRouter(prefix="/v1", tags=["Report"])

response_generator = ResponseGenerator()
session_store = SessionStore()


@router.post("/report", response_model=ReportResponseModel, dependencies=[Depends(verify_jarvis_token)])
def report_endpoint(request: ReportRequestModel) -> ReportResponseModel:
    response = response_generator.generate_report_response(request)
    session_store.add_task_summary(request.session_id, request.task_id, response.message)
    return response
