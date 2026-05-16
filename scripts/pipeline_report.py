#!/usr/bin/env python3
"""
pipeline_report.py — LAIGO nightly test pipeline analyzer.

Usage:
    python scripts/pipeline_report.py [--days 30] [--no-cache]

Requires GITHUB_TOKEN in .env (project root) or as GITHUB_TOKEN env var.
Create a token at https://github.com/settings/tokens with scope: repo
(or a fine-grained PAT with Actions: Read-only on gjGameJam/AutomationLaigo).
"""

import argparse
import io
import json
import os
import shutil
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from collections import defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path

REPO = "gjGameJam/AutomationLaigo"
WORKFLOW_FILE = "nightly.yml"
CACHE_DIR = Path(__file__).parent / ".pipeline_cache"
TRX_NS = {"ms": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


# ── Token ─────────────────────────────────────────────────────────────────────


def load_token() -> str:
    token = os.environ.get("GITHUB_TOKEN", "")
    if not token:
        env_file = Path(__file__).parent.parent / ".env"
        if env_file.exists():
            for line in env_file.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if line.startswith("GITHUB_TOKEN=") and not line.startswith("#"):
                    token = line.split("=", 1)[1].strip().strip('"').strip("'")
                    break
    if not token:
        print("ERROR: GITHUB_TOKEN not found.")
        print("  Add to .env:  GITHUB_TOKEN=ghp_your_token_here")
        print("  Create token: https://github.com/settings/tokens")
        print("  Scope needed: repo  (or fine-grained: Actions = Read-only)")
        sys.exit(1)
    return token


# ── GitHub API ────────────────────────────────────────────────────────────────


def _gh_request(url: str, token: str) -> urllib.request.Request:
    return urllib.request.Request(
        url,
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "laigo-pipeline-report/1.0",
        },
    )


def gh_get(path: str, token: str) -> dict | list:
    url = f"https://api.github.com{path}"
    try:
        with urllib.request.urlopen(_gh_request(url, token)) as resp:
            return json.loads(resp.read())
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"GitHub API {e.code} for {url}: {e.read().decode()}") from e


def gh_download_bytes(url: str, token: str) -> bytes:
    """Download a URL that may redirect (artifact ZIP download)."""
    try:
        with urllib.request.urlopen(_gh_request(url, token)) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        if e.code == 410:
            return b""  # Artifact expired
        raise RuntimeError(f"Download failed {e.code} for {url}") from e


# ── Workflow runs ─────────────────────────────────────────────────────────────


def get_workflow_id(token: str) -> int:
    workflows = gh_get(f"/repos/{REPO}/actions/workflows", token)
    for wf in workflows.get("workflows", []):
        if wf["path"].endswith(WORKFLOW_FILE):
            return wf["id"]
    raise ValueError(f"Workflow '{WORKFLOW_FILE}' not found in {REPO}")


def get_runs(workflow_id: int, days: int, token: str) -> list[dict]:
    since = (datetime.now(timezone.utc) - timedelta(days=days)).strftime("%Y-%m-%dT%H:%M:%SZ")
    runs, page = [], 1
    while True:
        data = gh_get(
            f"/repos/{REPO}/actions/runs?workflow_id={workflow_id}"
            f"&per_page=100&page={page}&created=%3E{since}",
            token,
        )
        batch = data.get("workflow_runs", [])
        runs.extend(batch)
        if len(batch) < 100:
            break
        page += 1
    return runs


# ── TRX parsing ───────────────────────────────────────────────────────────────


def parse_trx(trx_xml: str) -> list[dict]:
    root = ET.fromstring(trx_xml)
    results = []
    for r in root.findall(".//ms:UnitTestResult", TRX_NS):
        error_msg, stack = "", ""
        error_info = r.find(".//ms:ErrorInfo", TRX_NS)
        if error_info is not None:
            msg_el = error_info.find("ms:Message", TRX_NS)
            st_el = error_info.find("ms:StackTrace", TRX_NS)
            error_msg = (msg_el.text or "").strip() if msg_el is not None else ""
            stack = (st_el.text or "").strip() if st_el is not None else ""
        results.append(
            {
                "name": r.get("testName", ""),
                "outcome": r.get("outcome", ""),  # Passed | Failed | NotExecuted
                "duration": r.get("duration", ""),
                "error": error_msg,
                "stack": stack,
            }
        )
    return results


def _trx_from_zip_bytes(zip_bytes: bytes) -> str | None:
    """Extract TRX content from an artifact ZIP (may be outer + inner ZIP)."""
    if not zip_bytes:
        return None
    try:
        with zipfile.ZipFile(io.BytesIO(zip_bytes)) as outer:
            for name in outer.namelist():
                if name.endswith(".trx"):
                    return outer.read(name).decode("utf-8", errors="replace")
            # Artifact may be a ZIP-of-ZIP (nested)
            for name in outer.namelist():
                if name.endswith(".zip"):
                    try:
                        with zipfile.ZipFile(io.BytesIO(outer.read(name))) as inner:
                            for inner_name in inner.namelist():
                                if inner_name.endswith(".trx"):
                                    return inner.read(inner_name).decode("utf-8", errors="replace")
                    except zipfile.BadZipFile:
                        pass
    except zipfile.BadZipFile:
        pass
    return None


# ── Per-run data ──────────────────────────────────────────────────────────────


def get_run_results(run_id: int, token: str) -> list[dict] | None:
    """Return parsed test results for a run. Returns None if no artifact."""
    cache_file = CACHE_DIR / f"{run_id}.json"
    if cache_file.exists():
        return json.loads(cache_file.read_text(encoding="utf-8"))

    try:
        artifacts_data = gh_get(f"/repos/{REPO}/actions/runs/{run_id}/artifacts", token)
    except RuntimeError as e:
        print(f"  WARNING: {e}")
        return None

    trx_artifact = next(
        (a for a in artifacts_data.get("artifacts", []) if a["name"].startswith("test-results")),
        None,
    )
    if trx_artifact is None:
        return None

    zip_bytes = gh_download_bytes(trx_artifact["archive_download_url"], token)
    trx_content = _trx_from_zip_bytes(zip_bytes)
    if trx_content is None:
        return None

    results = parse_trx(trx_content)
    CACHE_DIR.mkdir(exist_ok=True)
    cache_file.write_text(json.dumps(results, indent=2), encoding="utf-8")
    return results


# ── Report ────────────────────────────────────────────────────────────────────


_CONCLUSION_ICON = {
    "success": "✓",
    "failure": "✗",
    "cancelled": "⊘",
    "timed_out": "⏱",
    "skipped": "—",
}


def short_name(full_name: str) -> str:
    return full_name.split(".")[-1] if "." in full_name else full_name


def generate_report(days: int = 30) -> None:
    token = load_token()

    print(f"Fetching workflow info for {REPO}...")
    workflow_id = get_workflow_id(token)
    runs = get_runs(workflow_id, days, token)
    if not runs:
        print(f"No runs found in the last {days} days.")
        return

    runs.sort(key=lambda r: r["created_at"], reverse=True)

    test_history: dict[str, list[dict]] = defaultdict(list)
    run_summaries: list[dict] = []

    print(f"Processing {len(runs)} run(s)...")
    for run in runs:
        run_id = run["id"]
        run_date = run["created_at"][:10]
        conclusion = run.get("conclusion") or "in_progress"
        results = get_run_results(run_id, token)

        summary: dict = {
            "run_id": run_id,
            "date": run_date,
            "conclusion": conclusion,
            "url": run["html_url"],
            "test_count": 0,
            "passed": 0,
            "failed": 0,
            "skipped": 0,
            "results": results or [],
        }
        if results:
            for r in results:
                outcome = r["outcome"]
                test_history[r["name"]].append(
                    {"run_id": run_id, "date": run_date, "outcome": outcome, "error": r["error"]}
                )
                summary["test_count"] += 1
                if outcome == "Passed":
                    summary["passed"] += 1
                elif outcome == "Failed":
                    summary["failed"] += 1
                else:
                    summary["skipped"] += 1
        run_summaries.append(summary)

    # ── Header ────────────────────────────────────────────────────────────────

    print()
    print("=" * 72)
    print(f"  LAIGO Nightly Pipeline Report — {REPO}")
    print(f"  Generated {datetime.now().strftime('%Y-%m-%d %H:%M')} | Last {days} days | {len(runs)} run(s)")
    print("=" * 72)

    # ── Last run ──────────────────────────────────────────────────────────────

    last = run_summaries[0]
    icon = _CONCLUSION_ICON.get(last["conclusion"], "?")
    print(f"\nLAST RUN  {last['date']}  [{icon} {last['conclusion'].upper()}]")
    print(f"  {last['url']}")
    if last["test_count"]:
        line = f"  {last['passed']}/{last['test_count']} passed"
        if last["failed"]:
            line += f"  |  {last['failed']} FAILED"
        if last["skipped"]:
            line += f"  |  {last['skipped']} skipped"
        print(line)
        if last["failed"]:
            print("\n  Failed tests:")
            for r in last["results"]:
                if r["outcome"] == "Failed":
                    print(f"    ✗  {short_name(r['name'])}")
                    if r["error"]:
                        first_line = r["error"].split("\n")[0][:100]
                        print(f"       {first_line}")
    else:
        print("  (no test results — run may have failed before tests executed)")

    # ── Run history table ─────────────────────────────────────────────────────

    print(f"\nRUN HISTORY")
    for s in run_summaries:
        icon = _CONCLUSION_ICON.get(s["conclusion"], "?")
        if s["test_count"]:
            test_info = f"{s['passed']}/{s['test_count']}"
            if s["failed"]:
                test_info += f" ({s['failed']} failed)"
        else:
            test_info = "no results"
        print(f"  {s['date']}  {icon}  {test_info:<22}  #{s['run_id']}")

    # ── Flakiness analysis ────────────────────────────────────────────────────

    if not test_history:
        print("\n(No test results available for flakiness analysis.)")
        print()
        print("=" * 72)
        return

    print("\nFLAKINESS ANALYSIS")

    always_passing: list[tuple] = []
    always_failing: list[tuple] = []
    flaky: list[tuple] = []

    for name, history in sorted(test_history.items()):
        pass_count = sum(1 for h in history if h["outcome"] == "Passed")
        fail_count = sum(1 for h in history if h["outcome"] == "Failed")
        total = len(history)
        if fail_count == 0:
            always_passing.append((name, total))
        elif pass_count == 0:
            always_failing.append((name, fail_count, total, history))
        else:
            flaky.append((name, fail_count, total, history))

    if flaky:
        flaky.sort(key=lambda x: x[1], reverse=True)
        print(f"\n  FLAKY — {len(flaky)} test(s)  (passed in some runs, failed in others)")
        for name, fail_count, total, history in flaky:
            rate = fail_count / total * 100
            last_fail = next(
                (h["date"] for h in sorted(history, key=lambda x: x["date"], reverse=True)
                 if h["outcome"] == "Failed"),
                "unknown",
            )
            print(f"\n    ~  {short_name(name)}")
            print(f"       Failed {fail_count}/{total} runs ({rate:.0f}%)  |  last failure: {last_fail}")
            errors = list(dict.fromkeys(
                h["error"].split("\n")[0][:90]
                for h in history
                if h["outcome"] == "Failed" and h["error"]
            ))
            for e in errors[:2]:
                print(f"       Error: {e}")
    else:
        print("\n  No flaky tests detected across all runs.")

    if always_failing:
        always_failing.sort(key=lambda x: x[1], reverse=True)
        print(f"\n  CONSISTENTLY FAILING — {len(always_failing)} test(s)  (failed in every run with results)")
        for name, fail_count, total, history in always_failing:
            print(f"\n    ✗  {short_name(name)}  (failed {fail_count}/{total} runs)")
            errors = list(dict.fromkeys(
                h["error"].split("\n")[0][:90]
                for h in history if h["error"]
            ))
            for e in errors[:1]:
                print(f"       Error: {e}")

    print(f"\n  STABLE — {len(always_passing)} test(s) at 100% pass rate")

    print()
    print("=" * 72)


# ── Entry point ───────────────────────────────────────────────────────────────


def main() -> None:
    parser = argparse.ArgumentParser(description="LAIGO nightly pipeline report")
    parser.add_argument("--days", type=int, default=30, help="Days to look back (default: 30)")
    parser.add_argument("--no-cache", action="store_true", help="Clear cached TRX data before running")
    args = parser.parse_args()

    if args.no_cache and CACHE_DIR.exists():
        shutil.rmtree(CACHE_DIR)
        print("Cache cleared.")

    generate_report(days=args.days)


if __name__ == "__main__":
    main()
