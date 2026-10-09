from typing import Any

from fastapi import APIRouter, Depends

from jarvis_ai.api.auth import verify_jarvis_token
from jarvis_ai.llm.client import OllamaClient
from jarvis_ai.memory.session_store import SessionStore
from jarvis_ai.nlu.intent_parser import IntentParser
from jarvis_ai.planning.planner import Planner
from jarvis_ai.schemas.models import PlanRequestModel, PlanResponseModel

router = APIRouter(prefix="/v1", tags=["Plan"])

llm_client = OllamaClient()
intent_parser = IntentParser(llm_client)
planner = Planner(intent_parser)
session_store = SessionStore()


def set_planner_client(client: Any) -> None:
    global llm_client, intent_parser, planner
    llm_client = client
    intent_parser = IntentParser(llm_client)
    planner = Planner(intent_parser)


@router.post("/plan", response_model=PlanResponseModel, dependencies=[Depends(verify_jarvis_token)])
def create_plan_endpoint(request: PlanRequestModel) -> PlanResponseModel:
    session_store.add_message(request.session_id, "user", request.text)
    response = planner.create_plan(request)
    session_store.add_message(request.session_id, "assistant", response.message)
    return response
