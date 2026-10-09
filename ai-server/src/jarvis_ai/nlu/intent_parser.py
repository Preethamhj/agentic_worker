from jarvis_ai.core.config import get_settings
from jarvis_ai.core.errors import StructuredOutputError
from jarvis_ai.core.ids import generate_intent_id
from jarvis_ai.llm.client import ILlmClient
from jarvis_ai.llm.structured import generate_validated
from jarvis_ai.nlu.normalizer import normalize_text
from jarvis_ai.nlu.risk import detect_keyword_risk, max_risk
from jarvis_ai.nlu.rules import parse_rules_fast_path
from jarvis_ai.schemas.models import IntentModel, RiskModel


class IntentParser:

    def __init__(self, llm_client: ILlmClient) -> None:
        self.llm_client = llm_client
        self.settings = get_settings()

    def parse(self, text: str) -> IntentModel:
        normalized_text = normalize_text(text)

        # Step 1: Fast path rules check (<50ms)
        rule_intent = parse_rules_fast_path(normalized_text)
        if rule_intent:
            return rule_intent

        # Step 2: Fall back to LLM intent parsing
        kw_risk, kw_reasons = detect_keyword_risk(normalized_text)

        system_prompt = (
            "You are the NLU module of Jarvis. Parse the user request into an Intent object.\n"
            "Respond strictly in JSON matching the IntentModel schema.\n"
            "Action must be one of: fs.search_files, fs.create_folder, fs.create_file, app.launch, task.schedule_reminder, or 'unsupported'."
        )

        user_prompt = f"User request: {normalized_text}"

        try:
            llm_intent = generate_validated(
                client=self.llm_client,
                prompt=user_prompt,
                model_class=IntentModel,
                system_prompt=system_prompt,
                max_retries=2,
            )
            # Maximum advisory risk calculation: max(keyword, llm)
            final_risk = max_risk(kw_risk, llm_intent.risk.level)
            reasons = list(set(kw_reasons + llm_intent.risk.reasons))

            iid = llm_intent.intent_id if llm_intent.intent_id else generate_intent_id()

            return IntentModel(
                intent_id=iid,
                kind=llm_intent.kind,
                action=llm_intent.action,
                confidence=llm_intent.confidence,
                entities=llm_intent.entities,
                risk=RiskModel(level=final_risk, reasons=reasons),
                source="llm",
            )
        except (StructuredOutputError, Exception):  # noqa: BLE001
            # If LLM parsing fails or Ollama is unavailable, return an unsupported/fallback intent gracefully
            return IntentModel(
                intent_id=generate_intent_id(),
                kind="chat",
                action="unsupported",
                confidence=0.0,
                entities={},
                risk=RiskModel(level=kw_risk, reasons=kw_reasons),
                source="rules",
            )
