# Jarvis: Verified Windows Agent

Jarvis is a local-first Windows assistant that converts natural language into allowlisted tool calls, executes them only after safety checks and approval, and reports success only after an independent verification engine confirms post-execution OS state.

## Quickstart

### Prerequisites
- Windows 10/11
- .NET 8 SDK
- Python 3.11.x
- Ollama (`qwen2.5:7b-instruct`)

### 1. Environment Setup
Run the setup script to install dependencies and configure the environment:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-windows.ps1
```

### 2. Run All Verification Checks
Run the master CI check script to validate C# build & tests, Python pytest/ruff/mypy, and C# code formatting:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\check-all.ps1
```

## Repository Architecture & Contracts
- **`docs/DESIGN.md`**: Full design specification & system architecture.
- **`AGENTS.md`**: Master prompt & rules for coding agents.
- **`contracts/schemas/`**: Ground truth JSON Schemas for inter-component communication.
- **`contracts/examples/`**: Valid and invalid test fixtures.
