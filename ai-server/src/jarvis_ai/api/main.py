from typing import Any

from fastapi import FastAPI

from jarvis_ai import __version__
from jarvis_ai.api.routes_plan import router as plan_router
from jarvis_ai.api.routes_report import router as report_router
from jarvis_ai.llm.client import OllamaClient

app = FastAPI(
    title="Jarvis AI Server",
    version=__version__,
    docs_url=None,
    redoc_url=None,
)

app.include_router(plan_router)
app.include_router(report_router)

ollama_client = OllamaClient()


@app.get("/health")
def health_check() -> dict[str, Any]:
    llm_status = "ok" if ollama_client.is_healthy() else "down"
    return {
        "status": "ok",
        "version": __version__,
        "llm": llm_status,
    }
