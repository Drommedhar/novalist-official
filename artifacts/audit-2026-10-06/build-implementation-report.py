"""Combine reviewed workstream ledgers into an offline implementation report."""

import html
import json
import os
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path, PureWindowsPath
from urllib.parse import quote

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
UPDATED = datetime.now(timezone.utc).date().isoformat()


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


def write(name, data):
    (HERE / name).write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )


def root_progress():
    result = read("root-findings.json")
    result["updated"] = UPDATED
    write("progress-root.json", result)
    return result


def esc(value):
    return html.escape(str(value), quote=True)


def strings(value):
    if not value:
        return []
    return value if isinstance(value, list) else [value]


def portable_source_path(value):
    path = PureWindowsPath(value)
    if path.is_absolute() and len(path.parts) > 3 and path.parts[1].lower() == "git":
        repository = path.parts[2]
        if repository == "novalist-official":
            return Path(*path.parts[3:]).as_posix()
        if repository.startswith("novalist-"):
            return (Path("..") / repository / Path(*path.parts[3:])).as_posix()
    return value


def list_html(values):
    return (
        "<ul>" + "".join(f"<li>{esc(value)}</li>" for value in values) + "</ul>"
        if values
        else "<p>See the workstream verification below.</p>"
    )


def files_html(values):
    links = []
    for value in values:
        path = Path(value)
        if not path.is_absolute():
            path = ROOT / path
        target = quote(Path(os.path.relpath(path, HERE)).as_posix(), safe="/")
        links.append(f'<li><a href="{esc(target)}">{esc(value)}</a></li>')
    return (
        "<ul>" + "".join(links) + "</ul>"
        if links
        else "<p>See the workstream diff.</p>"
    )


def performance_html():
    performance = read("p01-desktop/summary.json")
    labels = {
        "rendererPeakWorkingSetMiB": "Renderer peak working set (MiB)",
        "navigationToShellAndBackendReadyMs": "Navigation to shell/backend ready (ms)",
        "launcherObservedReadyMs": "Launch observed by harness (ms)",
        "entryBackgroundParsingTraceMs": "Entry background parsing trace (ms)",
    }
    rows = []
    for key, label in labels.items():
        metric = performance["metrics"][key]
        rows.append(
            f"<tr><td>{label}</td><td>{metric['original']['median']:.2f}</td><td>{metric['fixed']['median']:.2f}</td></tr>"
        )
    return (
        """<section id="performance"><h2>Measured desktop impact</h2>
<p>P01 removes embedded Help screenshots from the initial JavaScript bundle. These recorded Windows measurements predate the screenshot refresh. The comparison kept its tested renderer source, backend, main process and preload identical; only the manual asset plugin changed.</p>
<div class="table-wrap"><table><thead><tr><th>Median metric</th><th>Original packaging</th><th>Corrected packaging</th></tr></thead><tbody>"""
        + "".join(rows)
        + """</tbody></table></div>
<p>Five alternating fresh-profile timing/memory pairs and three separate tracing pairs ran in unpackaged Electron 42.11.8 with a Debug backend. OS caches were retained. The trace measures a background parsing/streaming-compile interval, not pure parser CPU time. Launcher improvement was small and one pair was slower after the change; these measurements do not establish a general cold-start guarantee or iOS performance.</p>
<p>All 11 Help screenshots also decoded through the actual desktop Help UI from emitted local assets with the browser offline and external requests blocked. See the <a href="p01-desktop/summary.json">desktop method and raw-evidence index</a>.</p>
<p>Separately, the iPhone 17 Pro simulator opened all 47 actual Help pages from source <code>af744fbd</code> and decoded all 11 refreshed local screenshots at 1440×900 under browser-enforced HTTP(S) resource/connect blocking. This instrumented WKWebView result is supporting simulator evidence, not an iOS performance measurement or physical airplane-mode result. Physical iPhone/iPad performance and offline Help remain deferred. See the <a href="macos-validation/p01-refreshed-native-full/summary.json">refreshed simulator Help evidence</a>.</p></section>"""
    )


def load_workstreams(progress):
    workstreams = [
        read("progress-backend.json"),
        read("progress-extensions.json"),
        read("progress-renderer.json"),
        root_progress(),
    ]
    if (HERE / "progress-validation.json").exists():
        validation = read("progress-validation.json")
        workstreams.append(validation)
        for field in (
            "native_validation",
            "delivery",
            "acceptance_scope",
            "goal_status",
        ):
            if field in validation:
                progress[field] = validation[field]
    return workstreams


def merge_workstreams(workstreams):
    by_id = {}
    checks = []
    for workstream in workstreams:
        checks.extend(
            check
            if isinstance(check, dict)
            else {
                "command": "Workstream verification",
                "status": "recorded",
                "detail": check,
            }
            for check in workstream.get("checks", [])
        )
        for entry in workstream.get(
            "findings", workstream.get("items", [])
        ) + workstream.get("additional", []):
            identifier = "EXT-005" if entry["id"] == "EXT005" else entry["id"]
            merged = dict(entry)
            if entry.get("summary"):
                merged["implementation"] = strings(entry.get("implementation")) + [
                    entry["summary"]
                ]
            if identifier in by_id:
                previous = by_id[identifier]
                for field in (
                    "implementation",
                    "files",
                    "tests",
                    "pending",
                    "deferred",
                ):
                    merged[field] = list(
                        dict.fromkeys(
                            strings(previous.get(field)) + strings(merged.get(field))
                        )
                    )
            by_id[identifier] = merged
    return by_id, checks


def update_finding(item, source, original):
    item["validation"] = strings(original.get("validation"))
    item.update(
        {
            key: source.get(key, [])
            for key in ("implementation", "files", "tests", "pending", "deferred")
        }
    )
    item["files"] = list(
        dict.fromkeys(portable_source_path(value) for value in strings(item["files"]))
    )
    item["pending"] = strings(item["pending"])
    item["deferred"] = strings(item["deferred"])
    status = source["status"]
    if item["deferred"] and not item["pending"]:
        item["status"] = "device-deferred"
    elif (
        status in ("complete", "implemented", "implemented-verified", "verified")
        and not item["pending"]
    ):
        item["status"] = "verified"
    elif item["id"] in (
        "M01",
        "M02",
        "M03",
        "M04",
        "M05",
        "M06",
        "R13",
        "R14",
        "R15",
        "P01",
    ):
        item["status"] = "native-pending"
    else:
        item["status"] = "verification-pending"


def merge_progress(progress, originals, workstreams):
    by_id, checks = merge_workstreams(workstreams)
    for item in progress["findings"]:
        update_finding(item, by_id[item["id"]], originals[item["id"]])
    progress["updated"] = UPDATED
    progress["checks"] = checks
    progress["limitations"] = [
        text for workstream in workstreams for text in workstream.get("limitations", [])
    ]


def goal_message(progress):
    goal_status = progress["goal_status"]
    goal_label = (
        "Blocked on native validation"
        if goal_status == "blocked"
        else "Goal remains active"
    )
    goal_note = (
        "The goal is blocked on the remaining required acceptance recorded in the native case ledger."
        if goal_status == "blocked"
        else "The goal remains active until required acceptance is complete."
    )
    if goal_status == "complete":
        if any(
            item["pending"] or item["status"] not in ("verified", "device-deferred")
            for item in progress["findings"]
        ):
            raise ValueError(
                "Cannot complete the report while required acceptance remains pending."
            )
        goal_label = "Current scope complete; device tests deferred"
        goal_note = "The requested work is complete within the revised scope. On 2026-10-07 the user requested continuation without physical hardware. Device acceptance remains deferred and unverified; no physical-device pass is claimed."
    return goal_label, goal_note


def finding_cards(progress, originals):
    cards = []
    for item in progress["findings"]:
        original = originals[item["id"]]
        body = (
            list_html(item["implementation"])
            if item["implementation"]
            else f"<p>{esc(original['recommendation'])}</p>"
        )
        cards.append(f'''<details class="finding" id="{esc(item["id"])}" data-status="{item["status"]}" data-owner="{esc(item["owner"])}">
<summary><div class="summary-top"><span class="badge {item["priority"].lower()}">{item["priority"]}</span><span>{esc(item["id"])}</span><span>{esc(item["status"].replace("-", " "))}</span></div><h3>{esc(item["title"])}</h3></summary>
<div class="finding-body"><h4>{"Implementation" if item["implementation"] else "Correction covered by these changes"}</h4>{body}
<h4>Relevant files</h4>{files_html(item["files"])}<h4>Original acceptance</h4><p>{esc(original["validation"])}</p><h4>Verification</h4>{list_html(item["tests"])}
{("<h4>Remaining acceptance</h4>" + list_html(item["pending"])) if item["pending"] else ""}
{("<h4>Deferred device acceptance (unverified)</h4>" + list_html(item["deferred"])) if item["deferred"] else ""}
<p><a href="audit.html#{esc(item["id"])}">Original audit evidence and reproduction</a></p></div></details>''')
    return cards


def render_report(progress, originals, goal):
    goal_label, goal_note = goal
    counts = Counter(item["status"] for item in progress["findings"])
    cards = finding_cards(progress, originals)
    checks = progress["checks"]
    checks_html = "".join(
        f"<tr><td>{esc(check.get('command', check.get('name', 'Check')))}</td><td>{esc(check.get('status', 'recorded'))}</td><td>{esc(check.get('detail', check.get('result', 'See workstream ledger')))}</td></tr>"
        for check in checks
    )
    css = (HERE / "report.css").read_text(encoding="utf-8")
    extra_css = "pre{overflow:auto;background:var(--nl-soft);padding:var(--nl-space-md);border-radius:var(--nl-radius)} details[hidden]{display:none} h4{margin-bottom:var(--nl-space-sm)}"
    native_handoff = (HERE / "native-handoff.fragment.html").read_text(encoding="utf-8")
    content = f"""<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Novalist audit implementation</title><style>{css}{extra_css}</style>
<a class="skip" href="#findings">Skip to findings</a><aside class="sidebar"><div class="brand">Novalist</div><p class="eyebrow">Audit implementation</p><nav><a href="#overview">Status</a><a href="#findings">All 73 findings</a><a href="#verification">Verification</a><a href="#native">macOS / iOS acceptance</a><a href="audit.html">Original audit</a></nav><footer>Updated {UPDATED}<br>Development branch handoff<br>{goal_label}</footer></aside><main>
<header id="overview"><p class="eyebrow">Desktop · extensions · iPhone · iPad</p><h1>Findings into <em>fixes.</em></h1><p class="lead">All 73 findings are tracked below. {counts["verified"]} findings have completed their recorded acceptance; {counts["device-deferred"]} have implemented fixes and supporting checks with physical-device acceptance deferred at the user's request. Native compilation, actual Apple CI and the recorded simulator scenarios passed. Original acceptance and exact evidence remain visible for every finding. No release is being published.</p>
<div class="metrics"><div class="metric"><strong>73</strong><span>Findings addressed</span></div><div class="metric"><strong>{counts["verified"]}</strong><span>Implemented and verified</span></div><div class="metric"><strong>{counts["device-deferred"]}</strong><span>Device acceptance deferred</span></div><div class="metric"><strong>{counts["native-pending"] + counts["verification-pending"]}</strong><span>Required checks pending</span></div></div></header>
<section><h2>What to review first</h2><div class="start-grid"><article class="start-card"><h3>Data and privacy</h3><p>Atomic saves, protected project creation, isolated pane buffers, checked conflict recovery, project-scoped AI state and export exclusions.</p></article><article class="start-card"><h3>Existing workflows</h3><p>Research persistence, imports, inline actions, speech cancellation, image loading, keyboard focus and extension teardown.</p></article></div><p class="note">{goal_note} All nine Mac verification checks and six portable temporary-access tests passed. Device cases remain unverified despite supporting simulator results; see the <a href="macos-validation/native-acceptance.json">native case ledger</a>. M05 corrects the original audit: a simulator job already existed; shared-code triggers were missing. Historical Aislop results below come from elevated scans after an access problem invalidated earlier unelevated results.</p></section>
<section id="findings"><h2>Per-finding ledger</h2><div class="controls"><div class="filters"><label class="search">Search<input id="search" type="search" placeholder="Finding ID, behavior, file or test"></label><label>Status<select id="status"><option value="">All</option><option value="verified">Verified</option><option value="device-deferred">Device acceptance deferred</option><option value="native-pending">Native pending</option><option value="verification-pending">Other verification pending</option></select></label></div><div class="actions"><button id="expand">Expand visible</button><button id="collapse">Collapse</button><button id="print">Print / PDF</button><a href="implementation-progress.json" download>Download ledger</a></div></div><p id="result-count" role="status" aria-live="polite">73 findings</p>{"".join(cards)}</section>
<section id="verification"><h2>Verification and limits</h2><p>Core and Backend suites reached 100% line coverage. SDK, extension, AI, worker and renderer checks are listed with their workstream results. Existing nonfixable Aislop maintainability warnings are retained in the scan artifacts; errors and fixable warnings must be resolved before completion.</p><div class="table-wrap"><table><thead><tr><th>Check</th><th>Status</th><th>Result</th></tr></thead><tbody>{checks_html}</tbody></table></div><p>No paid model requests, real model downloads, GPU inference or live publishing were used. Four existing AI hardware tests are skipped. The 16 MiB mobile preview limit bounds base64 allocations; oversized files retain their project data and show a preview error.</p></section>
{native_handoff}</main>
<script>const rows=[...document.querySelectorAll('.finding')];const search=document.querySelector('#search');const status=document.querySelector('#status');function filter(){{const query=search.value.trim().toLowerCase();let count=0;for(const row of rows){{row.hidden=!!((status.value&&row.dataset.status!==status.value)||(query&&!row.textContent.toLowerCase().includes(query)));if(!row.hidden)count++;}}document.querySelector('#result-count').textContent=count+' findings';}}search.addEventListener('input',filter);status.addEventListener('change',filter);document.querySelector('#expand').onclick=()=>rows.filter(row=>!row.hidden).forEach(row=>row.open=true);document.querySelector('#collapse').onclick=()=>rows.forEach(row=>row.open=false);document.querySelector('#print').onclick=()=>window.print();let before=[];window.addEventListener('beforeprint',()=>{{before=rows.map(row=>row.open);rows.filter(row=>!row.hidden).forEach(row=>row.open=true)}});window.addEventListener('afterprint',()=>rows.forEach((row,index)=>row.open=before[index]));function reveal(){{const row=document.getElementById(location.hash.slice(1));if(row?.classList.contains('finding')){{search.value='';status.value='';filter();row.open=true;row.scrollIntoView();}}}}window.addEventListener('hashchange',reveal);reveal();</script></html>"""
    runtime_checks = """<section id="runtime"><h2>Additional runtime evidence</h2><ul>
<li><strong>R17, verified:</strong> Both strengthened image-insertion scenarios pass on Windows and native Linux Electron under Debian WSLg. Normal and page view open the real context menu, hold the OS picker result, rebuild prose and clear selection before insertion. The isolated Linux harness uses <code>--no-sandbox</code> because WSL runs as root; this is not an application setting. See <a href="linux-validation/summary.json">Linux runtime evidence</a>.</li>
<li><strong>EXT-014–016, verified:</strong> Named contract tests prove fatal errors settle requests while the worker pipe remains open, missing or partial model weights report preparation required, and cancellation after the first passage prevents later generation and permits the next request. A real Python subprocess also returned a fatal error and completion within 109 ms and accepted the next request. Offline-cache restart denies socket connections. See <a href="speech-acceptance-evidence.json">speech acceptance evidence</a>. GPU inference remains an optional smoke check; it is not required by these original acceptance criteria.</li>
<li><strong>EXT-018, verified:</strong> Pen &amp; Paper loads through the native v1.7 and last native v1.14.5 host loaders. Qwen loads through its untagged 1.15-dev host (commit <code>8ffe1bf1</code>), including speech-provider registration and removal. Modern hosts reject the legacy manifests. These are loader-contract checks; native extension UI and model inference were not exercised, and the bounds do not certify every intervening historical release. See <a href="legacy-historical-validation/summary.json">historical host evidence</a>.</li>
</ul><p>Formats, Insight, Publish and Toolkit now require the next host, 3.5.4 or newer, because their corrected paths use new SDK members. Extension packaging is pinned to the future <code>v3.5.4</code> host tag and must wait for that host release. AI Assistant independently builds and loads against the clean, already released 3.5.3 SDK contract.</p><p>The legacy PnP and Qwen dev branches contain compatibility metadata and documentation only. Their pre-existing implementation work remains local. Qwen loader evidence used the preserved, unpublished 0.4 fixture; the pushed metadata retains version 0.3. Historical source links may therefore require that original fixture. See <a href="publication-extensions.json">repository delivery details</a>.</p></section>"""
    content = content.replace(
        '<section id="native">',
        performance_html() + runtime_checks + '<section id="native">',
    )
    return embed_ledger(content, progress)


def embed_ledger(content, progress):
    ledger_json = json.dumps(progress, ensure_ascii=False).replace("<", "\\u003c")
    offline_download = (
        '<script id="ledger" type="application/json">'
        + ledger_json
        + """</script><script>
document.querySelector('a[download]').addEventListener('click', event => {
  event.preventDefault();
  const url = URL.createObjectURL(new Blob([document.querySelector('#ledger').textContent], {type:'application/json'}));
  const link = document.createElement('a'); link.href=url; link.download='novalist-implementation-progress.json'; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});</script>"""
    )
    content = content.replace("</html>", offline_download + "</html>")
    return content


def link_original_audit():
    original = HERE / "audit.html"
    audit_html = original.read_text(encoding="utf-8")
    if 'id="implementation-link"' not in audit_html:
        opening = '<main id="main">'
        if opening not in audit_html:
            raise ValueError("The original audit main element is missing.")
        audit_html = audit_html.replace(
            opening,
            opening
            + '<p class="note" id="implementation-link">Implementation is tracked in the <a href="implementation.html">73-finding implementation report</a>. This audit preserves the original pre-fix evidence.</p>',
            1,
        )
        original.write_text(audit_html, encoding="utf-8")


def main():
    audit = read("findings.json")
    originals = {item["id"]: item for item in audit["findings"]}
    progress = read("implementation-progress.json")
    workstreams = load_workstreams(progress)
    merge_progress(progress, originals, workstreams)
    goal = goal_message(progress)
    write("implementation-progress.json", progress)
    content = render_report(progress, originals, goal)
    (HERE / "implementation.html").write_text(content, encoding="utf-8")
    link_original_audit()
    counts = Counter(item["status"] for item in progress["findings"])
    print(
        json.dumps({"findings": len(progress["findings"]), "status": counts}, indent=2)
    )


if __name__ == "__main__":
    main()
