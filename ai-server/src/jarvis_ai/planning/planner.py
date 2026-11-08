from typing import Literal

import httpx
import ollama

from jarvis_ai.core.errors import StructuredOutputError
from jarvis_ai.core.ids import generate_intent_id, generate_step_id, generate_trace_id
from jarvis_ai.nlu.intent_parser import IntentParser
from jarvis_ai.planning.decomposer import Decomposer
from jarvis_ai.planning.tool_mapper import ToolMapper
from jarvis_ai.schemas.models import (
    IntentModel,
    PlanRequestModel,
    PlanResponseModel,
    PlanStepModel,
    RiskModel,
    ToolCallModel,
)
from jarvis_ai.verification_planning.planner import VerificationPlanner


class Planner:

    def __init__(self, intent_parser: IntentParser) -> None:
        self.intent_parser = intent_parser
        self.decomposer = Decomposer()
        self.tool_mapper = ToolMapper()
        self.verification_planner = VerificationPlanner()

    def create_plan(self, request: PlanRequestModel) -> PlanResponseModel:
        trace_id = generate_trace_id()
        try:
            intent: IntentModel = self.intent_parser.parse(request.text)
        except StructuredOutputError:
            return self._planning_failure(
                trace_id, "clarify", "The model could not produce a valid plan. Please rephrase your request."
            )
        except httpx.TimeoutException:
            return self._planning_failure(
                trace_id, "error", "The local Ollama model timed out. Check that it is responsive and try again."
            )
        except (httpx.RequestError, ollama.ResponseError):
            return self._planning_failure(
                trace_id, "error", "The local Ollama model is unavailable. Check Ollama and the configured model."
            )

        if intent.action == "unsupported":
            return PlanResponseModel(
                trace_id=trace_id,
                status="unsupported",
                intent=intent,
                steps=[],
                message=f"Request '{request.text}' is not supported",
            )

        sub_tasks = self.decomposer.decompose(intent)
        steps: list[PlanStepModel] = []

        for idx, task in enumerate(sub_tasks, start=1):
            tool_id = task["tool_id"]
            raw_args = task["args"]

            is_valid, status, err_msg, validated_args = self.tool_mapper.map_and_validate(
                tool_id, raw_args
            )

            if not is_valid:
                return PlanResponseModel(
                    trace_id=trace_id,
                    status=status,
                    intent=intent,
                    steps=[],
                    message=err_msg or "Failed to validate tool call",
                )

            verification_plan = self.verification_planner.build_verification_plan(
                tool_id, validated_args
            )

            step = PlanStepModel(
                step_id=generate_step_id(idx),
                order=idx,
                description=task.get("description", f"Step {idx}: {tool_id}"),
                depends_on=[],
                tool_call=ToolCallModel(tool_id=tool_id, args=validated_args),
                verification_plan=verification_plan,
                advisory_risk=intent.risk.level,
            )
            steps.append(step)

        return PlanResponseModel(
            trace_id=trace_id,
            status="ok",
            intent=intent,
            steps=steps,
            message="Plan generated successfully",
        )

    @staticmethod
    def _planning_failure(
        trace_id: str, status: Literal["clarify", "error"], message: str
    ) -> PlanResponseModel:
        return PlanResponseModel(
            trace_id=trace_id,
            status=status,
            intent=IntentModel(
                intent_id=generate_intent_id(),
                kind="chat",
                action="unsupported",
                confidence=0.0,
                entities={},
                risk=RiskModel(level="low", reasons=[]),
                source="llm",
            ),
            steps=[],
            message=message,
        )
