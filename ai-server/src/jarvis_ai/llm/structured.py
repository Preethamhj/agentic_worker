import json
from typing import TypeVar

from pydantic import BaseModel, ValidationError

from jarvis_ai.core.errors import StructuredOutputError
from jarvis_ai.llm.client import ILlmClient

T = TypeVar("T", bound=BaseModel)


def generate_validated(
    client: ILlmClient,
    prompt: str,
    model_class: type[T],
    system_prompt: str | None = None,
    max_retries: int = 2,
) -> T:
    current_prompt = prompt
    last_raw_output: str | None = None
    json_schema = model_class.model_json_schema()

    for attempt in range(max_retries + 1):
        raw_output = client.generate(
            prompt=current_prompt,
            system_prompt=system_prompt,
            json_schema=json_schema,
            temperature=0.0,
        )
        last_raw_output = raw_output

        try:
            parsed_json = json.loads(raw_output)
            validated_obj = model_class.model_validate(parsed_json)
            return validated_obj
        except (json.JSONDecodeError, ValidationError) as err:
            if attempt < max_retries:
                current_prompt = (
                    f"{prompt}\n\n"
                    f"Previous attempt produced invalid output: {raw_output}\n"
                    f"Validation error: {err}\n"
                    f"Please correct the JSON output strictly matching schema."
                )
            else:
                raise StructuredOutputError(
                    f"Failed to produce valid JSON after {max_retries + 1} attempts: {err}",
                    raw_output=last_raw_output,
                ) from err

    raise StructuredOutputError("Validation failed", raw_output=last_raw_output)
