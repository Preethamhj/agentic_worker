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

# 20 Labelled Prompts with Ground-Truth expected tool_id and expected args for Accuracy evaluation
PROMPTS_20_LABELLED = [
    # 1. fs.search_files
    {
        "text": "Please locate all documents ending with .pdf in my working directory",
        "expected_tool": "fs.search_files",
        "expected_args": {"pattern": "*.pdf"},
    },
    {
        "text": "Can you find any files named budget in my primary storage",
        "expected_tool": "fs.search_files",
        "expected_args": {"pattern": "*budget*"},
    },
    {
        "text": "Search for all python scripts residing in the source directory",
        "expected_tool": "fs.search_files",
        "expected_args": {"pattern": "*.py"},
    },
    {
        "text": "Please find all image files with extension .png",
        "expected_tool": "fs.search_files",
        "expected_args": {"pattern": "*.png"},
    },
    # 2. fs.create_folder
    {
        "text": "I need you to generate a new directory called ProjectArchives",
        "expected_tool": "fs.create_folder",
        "expected_args": {"path": "ProjectArchives"},
    },
    {
        "text": "Could you make a folder titled Backup_2026 for storing old logs",
        "expected_tool": "fs.create_folder",
        "expected_args": {"path": "Backup_2026"},
    },
    {
        "text": "Can you construct a folder named ClientData in Documents",
        "expected_tool": "fs.create_folder",
        "expected_args": {"path": "ClientData"},
    },
    {
        "text": "Create a folder named Experimental for testing features",
        "expected_tool": "fs.create_folder",
        "expected_args": {"path": "Experimental"},
    },
    # 3. fs.create_file
    {
        "text": "Please write a file named notes.txt containing important meeting notes",
        "expected_tool": "fs.create_file",
        "expected_args": {"path": "notes.txt", "content": "important meeting notes"},
    },
    {
        "text": "Save a new file called config.yaml with custom settings inside",
        "expected_tool": "fs.create_file",
        "expected_args": {"path": "config.yaml", "content": "custom settings inside"},
    },
    {
        "text": "Generate a text file named log.txt containing debug trace output",
        "expected_tool": "fs.create_file",
        "expected_args": {"path": "log.txt", "content": "debug trace output"},
    },
    {
        "text": "Write a file named readme.md with content # Project Overview",
        "expected_tool": "fs.create_file",
        "expected_args": {"path": "readme.md", "content": "# Project Overview"},
    },
    # 4. app.launch
    {
        "text": "Could you launch the text editor for me right now",
        "expected_tool": "app.launch",
        "expected_args": {"app_id": "notepad"},
    },
    {
        "text": "I want to start the painting application to draw a diagram",
        "expected_tool": "app.launch",
        "expected_args": {"app_id": "mspaint"},
    },
    {
        "text": "Open up the calculator utility for financial calculations",
        "expected_tool": "app.launch",
        "expected_args": {"app_id": "calc"},
    },
    {
        "text": "Start the notepad program for quick notes",
        "expected_tool": "app.launch",
        "expected_args": {"app_id": "notepad"},
    },
    # 5. task.schedule_reminder
    {
        "text": "Set up a calendar alert for team sync at 4pm today",
        "expected_tool": "task.schedule_reminder",
        "expected_args": {"message": "team sync"},
    },
    {
        "text": "Notify me about doctor appointment tomorrow morning",
        "expected_tool": "task.schedule_reminder",
        "expected_args": {"message": "doctor appointment"},
    },
    {
        "text": "I would like a reminder to submit the quarterly report later",
        "expected_tool": "task.schedule_reminder",
        "expected_args": {"message": "submit the quarterly report"},
    },
    {
        "text": "Remind me to buy groceries before leaving office",
        "expected_tool": "task.schedule_reminder",
        "expected_args": {"message": "buy groceries"},
    },
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
        model_prefix = model_name.split(":")[0]
        if model_prefix not in proc.stdout:
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


def evaluate_model(model_name: str) -> dict[str, Any]:
    raw_ollama_client = OllamaClient(model=model_name)
    if not raw_ollama_client.is_healthy():
        print(f"Ollama server or model '{model_name}' is unavailable.")
        return {}

    rule_count = 0
    llm_count = 0
    validities = []
    tool_matches = []
    arg_matches = []
    latencies = []
    total_retries = 0

    print(f"\n--- Evaluating Model: {model_name} against {len(PROMPTS_20_LABELLED)} labelled prompts ---")

    for i, item in enumerate(PROMPTS_20_LABELLED, start=1):
        prompt = item["text"]
        expected_tool = item["expected_tool"]
        expected_args = item["expected_args"]

        rule_intent = parse_rules_fast_path(prompt)
        if rule_intent is not None:
            rule_count += 1
            print(f"[{i}/{len(PROMPTS_20_LABELLED)}] FAST-PATH RULE MATCH - skipping LLM evaluation")
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

            # Evaluate Tool Accuracy
            t_correct = (res.action == expected_tool)
            tool_matches.append(1 if t_correct else 0)

            # Evaluate Argument Accuracy
            a_correct = True
            for k, expected_v in expected_args.items():
                actual_v = str(res.entities.get(k, "")).lower()
                expected_v_str = str(expected_v).lower().strip("*")
                if expected_v_str not in actual_v and actual_v not in expected_v_str:
                    a_correct = False
                    break
            arg_matches.append(1 if a_correct else 0)

            print(
                f"[{i}/{len(PROMPTS_20_LABELLED)}] SUCCESS ({elapsed:.1f}ms) - tool: {res.action} (match: {t_correct}), args match: {a_correct}"
            )
        except Exception as e:
            elapsed = (time.perf_counter() - start) * 1000.0
            retries = max(0, tracking_client.call_count - 1)
            total_retries += retries

            validities.append(0)
            tool_matches.append(0)
            arg_matches.append(0)
            latencies.append(elapsed)
            print(f"[{i}/{len(PROMPTS_20_LABELLED)}] FAILED in {elapsed:.1f}ms - error: {e}")

    validity_rate = (sum(validities) / len(validities)) * 100.0 if validities else 0.0
    tool_acc = (sum(tool_matches) / len(tool_matches)) * 100.0 if tool_matches else 0.0
    arg_acc = (sum(arg_matches) / len(arg_matches)) * 100.0 if arg_matches else 0.0
    latencies.sort()
    p50 = statistics.median(latencies) if latencies else 0.0
    p95 = latencies[int(len(latencies) * 0.95)] if latencies else 0.0

    stats = {
        "model": model_name,
        "total": len(PROMPTS_20_LABELLED),
        "rule_count": rule_count,
        "llm_count": llm_count,
        "validity_rate": validity_rate,
        "tool_accuracy": tool_acc,
        "arg_accuracy": arg_acc,
        "total_retries": total_retries,
        "p50": p50,
        "p95": p95,
    }

    print(f"\n================ Labelled Evaluation Results: {model_name} ================")
    print(f"Target Model:               {model_name}")
    print(f"Total Prompts Tested:       {len(PROMPTS_20_LABELLED)}")
    print(f"Prompts Matched by Rules:   {rule_count}")
    print(f"Prompts Sent to LLM:        {llm_count}")
    print(f"LLM JSON Validity Rate:     {validity_rate:.1f}%")
    print(f"LLM Tool Action Accuracy:   {tool_acc:.1f}%")
    print(f"LLM Argument Accuracy:      {arg_acc:.1f}%")
    print(f"Total LLM Retries:          {total_retries}")
    print(f"LLM Path p50 Latency:       {p50:.1f} ms")
    print(f"LLM Path p95 Latency:       {p95:.1f} ms")
    print("=========================================================================\n")

    return stats


def run_eval() -> None:
    settings = get_settings()
    configured_model = settings.model

    ollama_ok = check_and_ensure_ollama_model(configured_model)
    if not ollama_ok:
        print("Ollama environment check failed. Aborting evaluation.")
        sys.exit(1)

    # Evaluate configured primary model from settings.json
    evaluate_model(configured_model)


if __name__ == "__main__":
    run_eval()
