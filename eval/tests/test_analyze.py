import json
from pathlib import Path

from eval.analyze import analyze, collapse, wilson


def test_wilson_known_two_of_four() -> None:
    low, high = wilson(2, 4)
    assert round(low, 6) == 0.150039
    assert round(high, 6) == 0.849961


def test_collapse_majority_and_instability() -> None:
    rows = [
        {"case_id": "c1", "verifier": "D", "status": "FAILED", "fault": "F1", "latency_ms": 1},
        {"case_id": "c1", "verifier": "D", "status": "FAILED", "fault": "F1", "latency_ms": 2},
        {"case_id": "c1", "verifier": "D", "status": "VERIFIED", "fault": "F1", "latency_ms": 3},
    ]
    result = collapse(rows)
    assert result[0]["status"] == "FAILED"
    assert result[0]["instability"] == 1


def test_analyze_writes_outputs(tmp_path: Path) -> None:
    raw = tmp_path / "raw.jsonl"
    raw.write_text("\n".join(json.dumps({"case_id": f"c{i}", "verifier": "D", "fault": "F1", "status": "FAILED", "latency_ms": i}) for i in range(1, 4)), encoding="utf-8")
    out = tmp_path / "out"
    analyze(raw, out)
    assert (out / "summary.csv").exists()
    assert (out / "summary.md").exists()
