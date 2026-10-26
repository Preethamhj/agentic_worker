"""Analyze Jarvis evaluation JSONL using only the Python standard library."""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median
from typing import Any


def wilson(successes: int, trials: int, z: float = 1.959963984540054) -> tuple[float, float]:
    """Return a Wilson confidence interval for a binomial proportion."""
    if trials <= 0:
        return (0.0, 0.0)
    p = successes / trials
    denominator = 1 + z * z / trials
    centre = (p + z * z / (2 * trials)) / denominator
    radius = z * math.sqrt(p * (1 - p) / trials + z * z / (4 * trials * trials)) / denominator
    return (centre - radius, centre + radius)


def parse_expected_verdicts(protocol_path: Path) -> dict[tuple[str, str], str]:
    """Parse and expand the pre-registered markdown table in the protocol."""
    text = protocol_path.read_text(encoding="utf-8-sig")
    section = text.split("### Pre-registered expected verdicts", 1)[1].split("\n## ", 1)[0]
    result: dict[tuple[str, str], str] = {}
    for line in section.splitlines():
        if not line.startswith("|") or line.startswith("|---") or "Fault(s)" in line:
            continue
        cells = [cell.strip() for cell in line.strip("|").split("|")]
        if len(cells) != 5:
            continue
        for fault in re.findall(r"F\d+(?:a|b)?", cells[0]):
            for verifier, value in zip(("A", "B", "C", "D"), cells[1:], strict=True):
                result[(verifier, fault)] = "VERIFIED" if value == "V" else "FAILED"
    return result


def _majority(rows: list[dict[str, Any]]) -> dict[str, Any]:
    statuses = Counter(str(row["status"]) for row in rows)
    max_count = max(statuses.values())
    winners = sorted(status for status, count in statuses.items() if count == max_count)
    result = dict(rows[0])
    result["status"] = winners[0]
    result["instability"] = int(len(statuses) > 1)
    return result


def collapse(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Collapse repetitions per distinct case and preserve instability counts."""
    grouped: dict[tuple[str, str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        if row.get("row_type") == "metadata" or row.get("verifier") == "metadata":
            continue
        grouped[(str(row["task_id"]), str(row["fault"]), str(row["verifier"]))].append(row)
    return [_majority(group) for group in grouped.values()]


def _p95(values: list[float]) -> float:
    return values[min(len(values) - 1, math.ceil(len(values) * 0.95) - 1)] if values else 0


def _is_good(row: dict[str, Any]) -> bool:
    return str(row.get("ground_truth", "BAD")).upper() == "GOOD" or str(row.get("fault")) == "F0"


def analyze(input_path: Path, output_dir: Path) -> None:
    """Write summary.csv and summary.md for a raw evaluation file."""
    rows = [json.loads(line) for line in input_path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    cases = collapse(rows)
    expected = parse_expected_verdicts(Path(__file__).resolve().parents[1] / "docs" / "eval" / "PROTOCOL.md")
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in cases:
        grouped[(str(row["verifier"]), str(row["fault"]))].append(row)

    output_dir.mkdir(parents=True, exist_ok=True)
    with (output_dir / "summary.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["verifier", "fault", "cases", "detected", "detection_rate", "wilson_low", "wilson_high", "false_success_rate", "false_failure_rate", "median_latency_ms", "p95_latency_ms", "instability_count"])
        for verifier, fault in sorted(grouped):
            group = grouped[(verifier, fault)]
            detected = sum(str(row["status"]) != "VERIFIED" for row in group)
            bad = sum(not _is_good(row) for row in group)
            good = len(group) - bad
            false_success = sum(str(row["status"]) == "VERIFIED" for row in group if not _is_good(row))
            false_failure = sum(str(row["status"]) != "VERIFIED" for row in group if _is_good(row))
            latencies = sorted(float(row.get("latency_ms", 0)) for row in group)
            low, high = wilson(detected, len(group))
            writer.writerow([verifier, fault, len(group), detected, detected / len(group) if group else 0, low, high, false_success / bad if bad else 0, false_failure / good if good else 0, median(latencies) if latencies else 0, _p95(latencies), sum(int(row.get("instability", 0)) for row in group)])

    lines = ["# Evaluation summary", "", f"Distinct verifier/case rows: {len(cases)}", "", "## Control verdicts per verifier", "", "| Verifier | VERIFIED | FAILED | ESCALATE | ERROR |", "|---|---:|---:|---:|---:|"]
    for verifier in sorted({str(row["verifier"]) for row in cases}):
        counts = Counter(str(row["status"]) for row in cases if row["verifier"] == verifier and row["fault"] == "F0")
        lines.append(f"| {verifier} | {counts['VERIFIED']} | {counts['FAILED']} | {counts['ESCALATE']} | {counts['ERROR']} |")
    lines += ["", "## Detection by verifier and fault", "", "| Verifier | Fault | Cases | Detected (non-control) | False-success on BAD | False-failure on GOOD | Wilson 95% | Instability |", "|---|---|---:|---:|---:|---:|---|---:|"]
    for verifier, fault in sorted(grouped):
        group = grouped[(verifier, fault)]
        bad = sum(not _is_good(row) for row in group)
        good = len(group) - bad
        detected = sum(str(row["status"]) != "VERIFIED" for row in group) if fault != "F0" else 0
        false_success = sum(str(row["status"]) == "VERIFIED" for row in group if not _is_good(row))
        false_failure = sum(str(row["status"]) != "VERIFIED" for row in group if _is_good(row))
        low, high = wilson(detected, len(group))
        lines.append(f"| {verifier} | {fault} | {len(group)} | {detected} | {false_success}/{bad} | {false_failure}/{good} | [{low:.3f}, {high:.3f}] | {sum(int(row.get('instability', 0)) for row in group)} |")

    deviations: list[tuple[str, str, str, int]] = []
    for (verifier, fault), group in sorted(grouped.items()):
        wanted = expected.get((verifier, fault))
        status = str(group[0]["status"])
        if wanted is not None and status != wanted:
            label = "ESCALATE where F expected" if wanted == "FAILED" and status == "ESCALATE" else f"expected {wanted}, actual {status}"
            deviations.append((verifier, fault, label, len(group)))
    lines += ["", "## Deviations from pre-registration", "", "| Verifier | Fault | Cases | Deviation |", "|---|---|---:|---|"]
    lines.extend(f"| {v} | {f} | {n} | {label} |" for v, f, label, n in deviations)
    if not deviations:
        lines.append("| none | — | 0 | none |")

    per_case: dict[tuple[str, str], dict[str, dict[str, Any]]] = defaultdict(dict)
    for row in cases:
        per_case[(str(row["task_id"]), str(row["fault"]))][str(row["verifier"])] = row
    not_better: list[tuple[str, str, str, str]] = []
    for (task, fault), verifier_rows in sorted(per_case.items()):
        d = verifier_rows.get("D")
        if d is None:
            continue
        d_detected = str(d["status"]) != "VERIFIED"
        bad = not _is_good(d)
        peer_detected = any(str(row["status"]) != "VERIFIED" for name, row in verifier_rows.items() if name != "D")
        if (bad and str(d["status"]) == "VERIFIED") or ((not bad) and d_detected) or (peer_detected and not d_detected):
            peers = ", ".join(f"{name}={row['status']}" for name, row in sorted(verifier_rows.items()) if name != "D")
            not_better.append((task, fault, str(d["status"]), peers))
    lines += ["", "## Cases where D is not better", "", "| Task | Fault | D status | Peer statuses |", "|---|---|---|---|"]
    lines.extend(f"| {task} | {fault} | {status} | {peers} |" for task, fault, status, peers in not_better)
    if not not_better:
        lines.append("| none | — | — | none |")
    lines += ["", "## C scope", "", "Inside-scope faults are listed before outside-scope faults: F5, F11, F15, F17a, and F17b are outside C's predicate scope.", "", "## Instability", "", f"Instability count: {sum(int(row.get('instability', 0)) for row in cases)}."]
    (output_dir / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("raw", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    analyze(args.raw, args.out)


if __name__ == "__main__":
    main()
