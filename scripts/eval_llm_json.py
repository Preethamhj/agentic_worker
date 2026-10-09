import json
import statistics
import subprocess
import sys
import time
from typing import Any

from jarvis_ai.core.config import get_settings
from jarvis_ai.llm.client import ILlmClient, OllamaClient
from jarvis_ai.llm.structured import generate_validated
from jarvis_ai.nlu.rules import parse_rules_fast_path
from jarvis_ai.schemas.models import IntentModel

# 20 Prompts specifically designed to BYPASS rule fast-path regex and hit the LLM path
PROMPTS_20_LLM_ONLY = [
    "Please locate all documents ending with .pdf in my working directory",
    "Can you find any files named budget in my primary storage",
    "I need you to generate a new directory called ProjectArchives",
    "Could you make a folder titled Backup_2026 for storing old logs",
    "Please write a file named notes.txt containing important meeting notes",
    "Save a new file called config.yaml with custom settings inside",
    "Could you launch the text editor for me right now",
    "I want to start the painting application to draw a diagram",
    "Set up a calendar alert for team sync at 4pm today",
    "Notify me about doctor appointment tomorrow morning",
    "Please find all image files with extension .png",
    "Can you construct a folder named ClientData in Documents",
    "Generate a text file named log.txt containing debug trace output",
    "Open up the calculator utility for financial calculations",
    "I would like a reminder to submit the quarterly report later",
    "Search for all python scripts residing in the source directory",
    "Create a folder named Experimental for testing features",
    "Write a file named readme.md with title # Project Overview",
    "Start the notepad program for quick notes",
    "Remind me to buy groceries before leaving office",
]


class RetryTrackingLlmClient(ILlmClient):

    def __init__(self, inner_client: OllamaClient) -> None:
        self.inner_client = inner_client
        self.call_count = 0

    def generate(
        self,
        prompt: str,
        system_prompt: str | None = None,
        json_schema: dict[str, Any] | None = None,
        temperature: float = 0.0,
    ) -> str:
        self.call_count += 1
        return self.inner_client.generate(
            prompt=prompt,
            system_prompt=system_prompt,
            json_schema=json_schema,
            temperature=temperature,
        )

    def is_healthy(self) -> bool:
        return self.inner_client.is_healthy()


def check_and_ensure_ollama_model(model_name: str) -> bool:
    print("=== Ollama Model Environment Verification ===")
    try:
        proc = subprocess.run(["ollama", "list"], capture_output=True, text=True, check=True)
        print("Installed Ollama Models:\n" + proc.stdout)
        if model_name.split(":")[0] not in proc.stdout:
            print(f"Model '{model_name}' not found locally. Pulling model...")
            pull_proc = subprocess.run(["ollama", "pull", model_name], check=True)
            if pull_proc.returncode != 0:
                print(f"Failed to pull model '{model_name}'.")
                return False
        else:
            print(f"Target model '{model_name}' is installed and ready.")
        return True
    except Exception as e:
        print(f"Error checking/pulling Ollama model '{model_name}': {e}")
        return False


def run_eval() -> None:
    settings = get_settings()
    model_name = settings.model

    ollama_ok = check_and_ensure_ollama_model(model_name)
    raw_ollama_client = OllamaClient()

    if not ollama_ok or not raw_ollama_client.is_healthy():
        print("Ollama server or model is unavailable. Aborting real model evaluation.")
        sys.exit(1)

    rule_count = 0
    llm_count = 0
    validities = []
    latencies = []
    total_retries = 0

    print(f"\nEvaluating {len(PROMPTS_20_LLM_ONLY)} prompts against real Ollama model ({model_name})...\n")

    for i, prompt in enumerate(PROMPTS_20_LLM_ONLY, start=1):
        rule_intent = parse_rules_fast_path(prompt)
        if rule_intent is not None:
            rule_count += 1
            print(f"[{i}/{len(PROMPTS_20_LLM_ONLY)}] FAST-PATH RULE MATCH - skipping LLM evaluation")
            continue

        llm_count += 1
        tracking_client = RetryTrackingLlmClient(raw_ollama_client)
        start = time.perf_counter()

        system_prompt = (
            "You are the NLU module of Jarvis. Parse the user request into an Intent object.\n"
            "Respond strictly in JSON matching the IntentModel schema.\n"
            "Action must be one of: fs.search_files, fs.create_folder, fs.create_file, app.launch, task.schedule_reminder, or 'unsupported'."
        )

        try:
            res = generate_validated(
                client=tracking_client,
                prompt=f"User request: {prompt}",
                model_class=IntentModel,
                system_prompt=system_prompt,
                max_retries=2,
            )
            elapsed = (time.perf_counter() - start) * 1000.0
            retries = max(0, tracking_client.call_count - 1)
            total_retries += retries

            validities.append(1)
            latencies.append(elapsed)
            print(
                f"[{i}/{len(PROMPTS_20_LLM_ONLY)}] LLM SUCCESS in {elapsed:.1f}ms (retries: {retries}) - action: {res.action}"
            )
        except Exception as e:
            elapsed = (time.perf_counter() - start) * 1000.0
            retries = max(0, tracking_client.call_count - 1)
            total_retries += retries

            validities.append(0)
            latencies.append(elapsed)
            print(f"[{i}/{len(PROMPTS_20_LLM_ONLY)}] LLM FAILED in {elapsed:.1f}ms - error: {e}")

    validity_rate = (sum(validities) / len(validities)) * 100.0 if validities else 0.0
    latencies.sort()
    p50 = statistics.median(latencies) if latencies else 0.0
    p95 = latencies[int(len(latencies) * 0.95)] if latencies else 0.0

    print("\n================ Real LLM-Path Evaluation Results ================")
    print(f"Target Model:               {model_name}")
    print(f"Total Prompts Tested:       {len(PROMPTS_20_LLM_ONLY)}")
    print(f"Prompts Matched by Rules:   {rule_count}")
    print(f"Prompts Sent to LLM:        {llm_count}")
    print(f"LLM JSON Validity Rate:     {validity_rate:.1f}%")
    print(f"Total LLM Retries:          {total_retries}")
    print(f"LLM Path p50 Latency:       {p50:.1f} ms")
    print(f"LLM Path p95 Latency:       {p95:.1f} ms")
    print("==================================================================\n")


if __name__ == "__main__":
    run_eval()
