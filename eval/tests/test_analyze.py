import json
from pathlib import Path

import pytest
from eval.analyze import analyze, collapse, parse_expected_verdicts, wilson


def test_wilson_known_two_of_four() -> None:
    low, high = wilson(2, 4)
    assert round(low, 6) == 0.150039
    assert round(high, 6) == 0.849961


def test_wilson_known_boundary_and_midpoint_values() -> None:
    assert wilson(0, 10, 1.96) == pytest.approx((0.0, 0.277540), abs=1e-6)
    assert wilson(10, 10, 1.96) == pytest.approx((0.722460, 1.0), abs=1e-6)
    assert wilson(5, 10, 1.96) == pytest.approx((0.236590, 0.763410), abs=1e-6)


def test_collapse_majority_and_instability() -> None:
    rows = [
        {"case_id": "c1-r1", "task_id": "t1", "verifier": "D", "status": "FAILED", "fault": "F1", "latency_ms": 1},
        {"case_id": "c1-r2", "task_id": "t1", "verifier": "D", "status": "FAILED", "fault": "F1", "latency_ms": 2},
        {"case_id": "c1-r3", "task_id": "t1", "verifier": "D", "status": "VERIFIED", "fault": "F1", "latency_ms": 3},
    ]
    result = collapse(rows)
    assert result[0]["status"] == "FAILED"
    assert result[0]["instability"] == 1


def test_collapse_uses_task_fault_as_distinct_case() -> None:
    rows = [
        {"case_id": "c-r1", "task_id": "task", "verifier": "D", "status": "FAILED", "fault": "F1", "latency_ms": 1},
        {"case_id": "c-r2", "task_id": "task", "verifier": "D", "status": "FAILED", "fault": "F1", "latency_ms": 2},
    ]
    assert len(collapse(rows)) == 1


def test_analyze_writes_outputs(tmp_path: Path) -> None:
    raw = tmp_path / "raw.jsonl"
    raw.write_text("\n".join(json.dumps({"case_id": f"c{i}", "task_id": f"t{i}", "verifier": "D", "fault": "F1", "status": "FAILED", "latency_ms": i}) for i in range(1, 4)), encoding="utf-8")
    out = tmp_path / "out"
    analyze(raw, out)
    assert (out / "summary.csv").exists()
    assert (out / "summary.md").exists()


def test_protocol_table_is_parsed_from_real_file() -> None:
    protocol = Path(__file__).parents[2] / "docs" / "eval" / "PROTOCOL.md"
    expected = parse_expected_verdicts(protocol)
    assert expected[("B", "F6")] == "VERIFIED"
    assert expected[("D", "F17b")] == "FAILED"


def test_analysis_reports_b_f6_deviation_and_control_table_first(tmp_path: Path) -> None:
    raw = tmp_path / "raw.jsonl"
    rows = []
    for verifier, status in (("A", "VERIFIED"), ("B", "FAILED"), ("C", "FAILED"), ("D", "FAILED")):
        rows.append({"task_id": "t1", "verifier": verifier, "fault": "F6", "ground_truth": "BAD", "status": status, "latency_ms": 1})
    for verifier in ("A", "B", "C", "D"):
        rows.append({"task_id": "t0", "verifier": verifier, "fault": "F0", "ground_truth": "GOOD", "status": "VERIFIED", "latency_ms": 1})
    raw.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")
    out = tmp_path / "out"
    analyze(raw, out)
    markdown = (out / "summary.md").read_text(encoding="utf-8")
    assert markdown.index("## Control verdicts per verifier") < markdown.index("## Deviations from pre-registration")
    assert "| B | F6 | 1 | expected VERIFIED, actual FAILED |" in markdown


def test_analysis_reports_d_not_better_and_false_rates(tmp_path: Path) -> None:
    raw = tmp_path / "raw.jsonl"
    rows = [
        {"task_id": "t1", "verifier": "A", "fault": "F1", "ground_truth": "BAD", "status": "FAILED", "latency_ms": 1},
        {"task_id": "t1", "verifier": "B", "fault": "F1", "ground_truth": "BAD", "status": "FAILED", "latency_ms": 1},
        {"task_id": "t1", "verifier": "C", "fault": "F1", "ground_truth": "BAD", "status": "FAILED", "latency_ms": 1},
        {"task_id": "t1", "verifier": "D", "fault": "F1", "ground_truth": "BAD", "status": "VERIFIED", "latency_ms": 1},
    ]
    raw.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")
    out = tmp_path / "out"
    analyze(raw, out)
    markdown = (out / "summary.md").read_text(encoding="utf-8")
    assert "## Cases where D is not better" in markdown
    assert "| t1 | F1 | VERIFIED |" in markdown
    assert "False-success on BAD" in markdown
