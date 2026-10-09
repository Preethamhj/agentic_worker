# ADR-0003: Local LLM Model Selection and Evaluation Protocol

* **Status:** Accepted
* **Date:** 2026-10-09
* **Authors:** Jarvis AI & Verification Team

## Context
Jarvis requires a local-first LLM running via Ollama on 127.0.0.1 for NLU intent parsing and plan generation. The model must produce strict JSON matching `contracts/schemas/Intent.schema.json` without emitting markdown formatting or invalid fields.

## Decision
1. **Primary Model Choice (`qwen2.5:7b-instruct`):**
   `qwen2.5:7b-instruct` is selected as the standard primary model in `config/settings.json` due to superior instruction-following, parameter extraction, and structured JSON output accuracy.

2. **Fallback Model (`llama3.2:latest`):**
   `llama3.2:latest` (2.0 GB) is supported as a fallback lightweight model for low-resource environments.

3. **Config-Driven Evaluation:**
   `scripts/eval_llm_json.py` reads the active target model directly from `config/settings.json`. The evaluation harness checks `ollama list` before execution, automatically pulling the model if missing.

4. **Labelled Benchmark Metrics:**
   Evaluation reports 5 distinct metrics across 20 non-rule prompts:
   - JSON Validity Rate (%)
   - Tool Action Accuracy Rate (%)
   - Argument Extraction Accuracy Rate (%)
   - Total Retries
   - Latency Profile (p50 and p95 ms)

## Consequences
- Guarantees alignment between configured settings (`config/settings.json`), runtime execution, and evaluation reporting.
- Eliminates hardcoded model assumptions across Python AI Server modules.
