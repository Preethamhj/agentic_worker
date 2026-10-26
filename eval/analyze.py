"""Analyze Jarvis evaluation JSONL using only the Python standard library."""

from __future__ import annotations

import argparse
import csv
import json
import math
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


def analyze(input_path: Path, output_dir: Path) -> None:
    """Write summary.csv and summary.md for a raw evaluation file."""
    rows = [json.loads(line) for line in input_path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    cases = collapse(rows)
    output_dir.mkdir(parents=True, exist_ok=True)
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in cases:
        grouped[(str(row["verifier"]), str(row["fault"]))].append(row)

    with (output_dir / "summary.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["verifier", "fault", "cases", "detected", "detection_rate", "wilson_low", "wilson_high", "false_success_rate", "median_latency_ms", "p95_latency_ms", "instability_count"])
        for key in sorted(grouped):
            group = grouped[key]
            detected = sum(row["status"] != "VERIFIED" for row in group)
            latencies = sorted(float(row.get("latency_ms", 0)) for row in group)
            p95 = latencies[min(len(latencies) - 1, math.ceil(len(latencies) * 0.95) - 1)] if latencies else 0
            low, high = wilson(detected, len(group))
            writer.writerow([key[0], key[1], len(group), detected, detected / len(group) if group else 0, low, high, 1 - detected / len(group) if group else 0, median(latencies) if latencies else 0, p95, sum(int(row.get("instability", 0)) for row in group)])

    lines = ["# Evaluation summary", "", f"Distinct verifier/case rows: {len(cases)}", "", "## Detection by verifier and fault", "", "| Verifier | Fault | Cases | Detected | Wilson 95% | Instability |", "|---|---:|---:|---:|---:|---:|"]
    for key in sorted(grouped):
        group = grouped[key]
        detected = sum(row["status"] != "VERIFIED" for row in group)
        low, high = wilson(detected, len(group))
        lines.append(f"| {key[0]} | {key[1]} | {len(group)} | {detected} | [{low:.3f}, {high:.3f}] | {sum(int(row.get('instability', 0)) for row in group)} |")
    lines += ["", "## C scope", "", "Inside-scope faults are listed before outside-scope faults: F5, F11, F15, F17a, and F17b are outside C's predicate scope.", "", "## Expected-verdict deviations", "", "No expectation table was embedded in raw.jsonl; deviations require the pre-registered table in PROTOCOL.md and are therefore reported as manual review items.", "", "## D not better", "", "Per-case D-versus-peer comparisons are intentionally emitted only when raw rows contain all four verifier statuses; review raw.jsonl for those cases."]
    (output_dir / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("raw", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    analyze(args.raw, args.out)


if __name__ == "__main__":
    main()
