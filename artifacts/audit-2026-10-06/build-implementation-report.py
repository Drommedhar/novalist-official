"""Combine reviewed workstream ledgers into an offline implementation report."""

import html
import json
import os
from collections import Counter
from datetime import date
from pathlib import Path, PureWindowsPath
from urllib.parse import quote

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
UPDATED = date.today().isoformat()


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


def write(name, data):
    (HERE / name).write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def root_progress():
    items = []

    def add(ids, change, files, tests, pending=()):
        for identifier in ids.split():
            items.append({"id": identifier, "status": "native-validation-pending" if pending else "implemented-verified",
                          "implementation": [change], "files": files, "tests": tests, "pending": list(pending)})

    add("A01 A02 A08", "Chat reads only selected privacy-filtered context. Project changes, reset, cancellation and disposal retire delayed context reads and responses.",
        ["../novalist-aiassistant/Services/ChatWebViewController.cs", "../novalist-aiassistant/ViewModels/AiChatViewModel.cs"],
        ["AuditChatLifecycleTests covers delayed provider/context replies, reset, project changes, queued UI chunks and prepared request reuse.",
         "Final complete AI suite: 157 passed and 4 existing hardware-dependent skips; chat webview state tests: 4 passed."])
    add("A03 A12", "Knowledge caches and locks use captured project-specific paths. A single cancellable lock owns each transaction; failed writes do not publish a changed cache.",
        ["../novalist-aiassistant/Services/CharacterKnowledgeService.cs"],
        ["CharacterKnowledgeServiceTests: cold/warm updates, concurrency, cancellation, project switches, failed writes and corrupt files."])
    add("A04 A05 A06 A09", "Requests capture model and cancellation independently. CLI access is serialized; ACP requests use isolated sessions and bounded waits. Empty SSE choice arrays preserve preceding text.",
        ["../novalist-aiassistant/Services/AiService.cs", "../novalist-aiassistant/Services/AnthropicClient.cs",
         "../novalist-aiassistant/Services/CopilotAcpClient.cs", "../novalist-aiassistant/Services/ClaudeCliClient.cs"],
        ["Full AI suite: 157 passed, 4 existing hardware-dependent tests skipped.",
         "Real fake-CLI child processes verify session/model arguments, cancellation, process exit, stdin and temporary-file cleanup."])
    add("A07", "One request assembler budgets instructions, selected context, current input and recent whole turns, with output reserve. Permanent history stores the writer's original input rather than repeating attached context.",
        ["../novalist-aiassistant/Services/ChatRequestAssembler.cs", "../novalist-aiassistant/web/chat.html"],
        ["ChatRequestAssemblerTests: bounded full requests, complete turn eviction, oversized required input and duplicate labels.",
         "Output reserve is sent to compatible HTTP and Anthropic providers; CLI output remains an estimate."])
    add("A10", "Preview inclusion tracks each occurrence independently, including repeated instances of one block; blank blocks are excluded.",
        ["../novalist-aiassistant/Services/ContextEngine.cs"], ["Duplicate-heading tests verify each block's inclusion independently."])
    add("A11", "PRs and release packaging share a test gate. SDK source and package versions are pinned, and the manifest declares the corresponding minimum host.",
        ["../novalist-aiassistant/.github/workflows/validate.yml", "../novalist-aiassistant/.github/workflows/release.yml",
         "../novalist-aiassistant/sdk-compatibility.json", "../novalist-aiassistant/tools/check-sdk-compatibility.py"],
        ["Compatibility contract check passes: host 3.5.3, SDK 13.4.0, revision ba8b6d328b64ded6e923baa25ce086d7e9e799e4.",
         "Clean pinned-SDK Release fixture: 157 passed, 4 existing skips; runtime smoke loaded 375 types and bound 13 host interfaces.",
         "Offline dictation worker suite: 14 passed."])
    add("P02", "The chat bridge sends response/thinking suffixes with an explicit reset flag. The webview batches DOM updates to animation frames and retains final messages.",
        ["../novalist-aiassistant/Services/ChatStreamUpdate.cs", "../novalist-aiassistant/web/chat.html"],
        ["Synthetic 12,000-character reply plus thinking transfers 12,006 content characters, preserving exact final text."])
    native = ["Complete the remaining case-by-case Apple runtime acceptance in macos-validation/native-acceptance.json. Native compilation passed on the Mac; the current continuation uses simulators only, so required physical-device acceptance remains deferred."]
    add("M01", "Native background events request a bounded, acknowledged flush. A project/book/draft-scoped journal retains scene, manuscript and Research edits for checked recovery after suspension or termination.",
        ["Novalist.Mobile/App.cs", "Novalist.Mobile/Pages/RendererHostPage.Lifecycle.cs", "app/src/renderer/src/mobile/recovery.ts",
         "app/src/renderer/src/mobile/recoveryJournal.ts", "app/src/renderer/src/shell/useWorkspaceWindow.ts",
         "app/src/renderer/src/views/library/useResearchDrafts.ts", "app/scripts/research-persistence.test.mjs"],
        ["Portable journal tests cover scope, retained edits and conflict-safe recovery; shim tests acknowledge only completed background saves.",
         "Research persistence regression holds an old hook's save across unmount/remount and proves its acknowledgement cannot erase the newer journal entry; all dirty notes remain retryable after a failed flush."], native)
    add("M02", "Folder grants are released on failed acquisition, moved-path rejection, project changes and teardown. Moved parent bookmarks retain aliases for unopened sibling projects. Temporary manuscript grants belong to one renderer page, including late picker replies after reload.",
        ["Novalist.Mobile/Services/SecurityScopedFolders.cs", "Novalist.Mobile/Services/IosStoredPathResolver.cs",
         "Novalist.Mobile/Services/TemporaryAccessRegistry.cs", "Novalist.Mobile/Services/SecurityScopedFolders.Manuscripts.cs",
         "Novalist.Backend/Workspace.Library.cs"], ["Actual native folder pick/cancel/reselect/create/open/close, manuscript preview/cancel/reselect/Import/Close, late old-page picker isolation after real WebContent termination, and full moved-parent sibling-project recents/identity passed. Native counts ended 9/9/zero retained in move run and 4/4/zero retained in actual import follow-up. All probes removed and clean build/signature/install/native UI/relaunch passed. Evidence: macos-validation/m02/summary.json and macos-validation/m02-ui/summary.json."])
    add("M03", "Container relocation applies only to recorded app-owned projects with matching stable IDs. Every repaired path is persisted as an exact trusted alias before recents are updated; serialized atomic transactions preserve concurrent records and successive relocations.",
        ["Novalist.Mobile/Services/OwnedProjectPaths.cs", "Novalist.Mobile/Services/IosStoredPathResolver.cs"],
        ["MobileOwnedProjectPathsTests: 9 passed, including successive container moves, concurrent registry writes, malicious paths and corrupt/missing candidate manifests.",
         "A real iPad simulator data-container move preserved identical app bytes and stable project/book/draft identities; old-path records updated after reopening and Research/Manuscript markers survived. Two further actual moves without opening the project between repairs preserved aliases and stable identities; see macos-validation/m03-successive/summary.json. Actual Files-visible external matching-suffix relocation also passed; see macos-validation/m03-external/summary.json. Physical relocation remains deferred. Evidence: simulator-ui/container-relocation.json."], native)
    add("M04", "The bridge waits for readiness and acknowledges delivery. Timeouts/disconnect reject outstanding calls without replaying writes; stale ports are retired. The iOS host now awaits WebKit evaluation directly so native JavaScript and terminated-process errors reach the recovery path instead of escaping MAUI's async-void mapper and aborting the app. WebKit failure pauses editing and offers reload after a bounded notification attempt.",
        ["Novalist.Mobile/Pages/RendererHostPage.Transport.cs", "Novalist.Mobile/Pages/RendererHostPage.cs",
         "Novalist.Mobile/Pages/RendererHostPage.JavaScript.cs", "Novalist.Mobile/Pages/RendererHostPage.Lifecycle.cs",
         "app/src/renderer/src/mobile/hostCalls.ts", "app/src/renderer/src/mobile/shim.ts"],
        ["Mobile host-call and shim tests: 10 passed, including readiness, stale ports, timeout, disconnect and lifecycle acknowledgement.",
         "Actual associated WebContent termination reproduced a host abort before the evaluator correction. With the correction, a native JavaScript NSError rejects the pending call and shows Reload/Later; actual WebContent termination reaches the native alert without aborting the host. These transport subsets retain their instrumented source provenance. Evidence: macos-validation/m04-prefixed-webkit-crash.json and m04-fixed-*.json."],
        [])
    add("M05", "Expanded the existing Apple PR workflow to cover Core, SDK, renderer and shared build inputs. Audit correction: a simulator compile gate already existed; its path triggers were incomplete.",
        [".github/workflows/apple-speech.yml", "app/package.json", "app/package-lock.json", "README.md"],
        ["The Mac verification script passed unsigned simulator compilation and device speech-library compilation at clean revision 0558dda71a395b6ab6208dd6820cc19811a61430. Sanitized evidence: macos-validation/native-compile/checks.tsv. Local compilation does not establish actual CI path-trigger behavior.",
         "Commit 0c789b29 was pushed to dev and restores Apple workflow dependencies with npm ci --no-audit. Actual Core-only, SDK-only and renderer-only Apple PR runs passed; an intentional iOS #error failed with CS1029, then probe removal produced a clean passing PR run. All four draft probe PRs were closed without merge. Evidence: macos-validation/m05-ci/summary.json. Signed physical-device smoke remains deferred under the simulator-only direction."], native)
    add("M06", "Native permission denial travels as a stable error code and becomes the browser NotAllowedError contract used by dictation.",
        ["Novalist.Mobile/Pages/RendererHostPage.cs", "app/src/renderer/src/mobile/hostCalls.ts", "app/scripts/mobile-dictation.test.mjs"],
        ["Portable microphone tests check NotAllowedError and execute the actual shim, HostCallChannel, microphone abstraction and dictation store. Denial shows dictation.permission; permitted retry records, stops exactly once and clears the stale error. Full source suite: 114 passed.",
         "Real simulator native permission denial, Settings grant, retry, explicit stop and background interruption passed with unchanged fixed app bytes. The permission change caused a fresh process; physical, call/route and same-process stale-error recovery remain pending. Evidence: simulator-ui/microphone-summary.json."], native)
    result = {"updated": UPDATED, "findings": items, "checks": [
        {"command": "dotnet test Core --filter MobileOwnedProjectPathsTests|MobileAssetReaderTests", "status": "passed", "detail": "8 tests passed. Oversized media is rejected before allocation; preview limit is 16 MiB."},
        {"command": "node --experimental-strip-types --test --test-isolation=none scripts/mobile-host-calls.test.mjs scripts/mobile-shim.test.mjs", "status": "passed", "detail": "10 passed."},
        {"command": "Elevated Aislop mobile/transport scan", "status": "passed", "detail": "Score 100; zero diagnostics. See aislop-mobile-current.json."},
        {"command": "AI full suite and clean pinned-SDK Release fixture", "status": "passed", "detail": "157 passed, 4 existing hardware skips in each run; minimum-host managed load smoke passed."},
        {"command": "node --test tests/chat-web.test.cjs", "status": "passed", "detail": "4 chat webview lifecycle tests passed; included in PR/release validation."},
        {"command": "Elevated Aislop AI changed files", "status": "passed-with-warnings", "detail": "Score 81; 0 errors, 0 fixable warnings; 32 nonfixable size, complexity and diagnostic warnings remain."},
        {"command": "Elevated Aislop official project scan", "status": "passed", "detail": "Score 100; 0 errors, 0 warnings, 0 fixable findings; 1,672 supported files. Whole-app ESLint findings are recorded separately using the app working directory."}
    ]}
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
    return "<ul>" + "".join(f"<li>{esc(value)}</li>" for value in values) + "</ul>" if values else "<p>See the workstream verification below.</p>"


def files_html(values):
    links = []
    for value in values:
        path = Path(value)
        if not path.is_absolute():
            path = ROOT / path
        target = quote(Path(os.path.relpath(path, HERE)).as_posix(), safe="/")
        links.append(f'<li><a href="{esc(target)}">{esc(value)}</a></li>')
    return '<ul>' + ''.join(links) + '</ul>' if links else '<p>See the workstream diff.</p>'


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
        rows.append(f"<tr><td>{label}</td><td>{metric['original']['median']:.2f}</td><td>{metric['fixed']['median']:.2f}</td></tr>")
    return '''<section id="performance"><h2>Measured desktop impact</h2>
<p>P01 removes embedded Help screenshots from the initial JavaScript bundle. This controlled Windows comparison keeps the current renderer source, backend, main process and preload identical; only the manual asset plugin changes.</p>
<div class="table-wrap"><table><thead><tr><th>Median metric</th><th>Original packaging</th><th>Corrected packaging</th></tr></thead><tbody>''' + ''.join(rows) + '''</tbody></table></div>
<p>Five alternating fresh-profile timing/memory pairs and three separate tracing pairs ran in unpackaged Electron 42.11.8 with a Debug backend. OS caches were retained. The trace measures a background parsing/streaming-compile interval, not pure parser CPU time. Launcher improvement was small and one pair was slower after the change; these measurements do not establish a general cold-start guarantee or iOS performance.</p>
<p>All 11 Help screenshots also decoded through the actual desktop Help UI from emitted local assets with the browser offline and external requests blocked. See the <a href="p01-desktop/summary.json">desktop method and raw-evidence index</a>.</p>
<p>Separately, the iPhone 17 Pro simulator opened all 47 actual Help pages and decoded all 11 local screenshots at 1440×900 under browser-enforced HTTP(S) blocking. This instrumented WKWebView result is supporting simulator evidence, not an iOS performance measurement or physical airplane-mode result. Physical iPhone/iPad performance and offline Help remain deferred. See the <a href="macos-validation/offline-help.json">simulator Help evidence</a>.</p></section>'''


def main():
    audit = read("findings.json")
    originals = {item["id"]: item for item in audit["findings"]}
    progress = read("implementation-progress.json")
    workstreams = [read("progress-backend.json"), read("progress-extensions.json"), read("progress-renderer.json"), root_progress()]
    if (HERE / "progress-validation.json").exists():
        validation = read("progress-validation.json")
        workstreams.append(validation)
        for field in ("native_validation", "delivery"):
            if field in validation:
                progress[field] = validation[field]
    by_id = {}
    checks = []
    for workstream in workstreams:
        checks.extend(check if isinstance(check, dict) else {"command": "Workstream verification", "status": "recorded", "detail": check}
                      for check in workstream.get("checks", []))
        for entry in workstream.get("findings", workstream.get("items", [])) + workstream.get("additional", []):
            identifier = "EXT-005" if entry["id"] == "EXT005" else entry["id"]
            merged = dict(entry)
            if entry.get("summary"):
                merged["implementation"] = strings(entry.get("implementation")) + [entry["summary"]]
            if identifier in by_id:
                previous = by_id[identifier]
                for field in ("implementation", "files", "tests", "pending"):
                    merged[field] = list(dict.fromkeys(strings(previous.get(field)) + strings(merged.get(field))))
            by_id[identifier] = merged
    for item in progress["findings"]:
        source = by_id[item["id"]]
        item["validation"] = strings(originals[item["id"]].get("validation"))
        item.update({key: source.get(key, []) for key in ("implementation", "files", "tests", "pending")})
        item["files"] = list(dict.fromkeys(portable_source_path(value) for value in strings(item["files"])))
        item["pending"] = strings(item["pending"])
        status = source["status"]
        if status in ("complete", "implemented", "implemented-verified", "verified") and not item["pending"]:
            item["status"] = "verified"
        elif item["id"] in ("M01", "M02", "M03", "M04", "M05", "M06", "R13", "R14", "R15", "P01"):
            item["status"] = "native-pending"
        else:
            item["status"] = "verification-pending"
    progress["updated"] = UPDATED
    goal_status = progress["goal_status"]
    goal_label = "Blocked on native validation" if goal_status == "blocked" else "Goal remains active"
    goal_note = ("The goal is blocked on the remaining required acceptance recorded in the native case ledger."
                 if goal_status == "blocked" else "The goal remains active until required acceptance is complete.")
    progress["checks"] = checks
    progress["limitations"] = [text for workstream in workstreams for text in workstream.get("limitations", [])]
    write("implementation-progress.json", progress)
    counts = Counter(item["status"] for item in progress["findings"])
    cards = []
    for item in progress["findings"]:
        original = originals[item["id"]]
        body = list_html(item["implementation"]) if item["implementation"] else f'<p>{esc(original["recommendation"])}</p>'
        cards.append(f'''<details class="finding" id="{esc(item['id'])}" data-status="{item['status']}" data-owner="{esc(item['owner'])}">
<summary><div class="summary-top"><span class="badge {item['priority'].lower()}">{item['priority']}</span><span>{esc(item['id'])}</span><span>{esc(item['status'].replace('-', ' '))}</span></div><h3>{esc(item['title'])}</h3></summary>
<div class="finding-body"><h4>{'Implementation' if item['implementation'] else 'Correction covered by these changes'}</h4>{body}
<h4>Relevant files</h4>{files_html(item['files'])}<h4>Original acceptance</h4><p>{esc(original['validation'])}</p><h4>Verification</h4>{list_html(item['tests'])}
{('<h4>Remaining acceptance</h4>' + list_html(item['pending'])) if item['pending'] else ''}
<p><a href="audit.html#{esc(item['id'])}">Original audit evidence and reproduction</a></p></div></details>''')
    checks_html = "".join(f"<tr><td>{esc(check.get('command', check.get('name', 'Check')))}</td><td>{esc(check.get('status', 'recorded'))}</td><td>{esc(check.get('detail', check.get('result', 'See workstream ledger')))}</td></tr>" for check in checks)
    css = (HERE / "report.css").read_text(encoding="utf-8")
    extra_css = "pre{overflow:auto;background:var(--nl-soft);padding:var(--nl-space-md);border-radius:var(--nl-radius)} details[hidden]{display:none} h4{margin-bottom:var(--nl-space-sm)}"
    native_handoff = (HERE / "native-handoff.fragment.html").read_text(encoding="utf-8")
    content = f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Novalist audit implementation</title><style>{css}{extra_css}</style>
<a class="skip" href="#findings">Skip to findings</a><aside class="sidebar"><div class="brand">Novalist</div><p class="eyebrow">Audit implementation</p><nav><a href="#overview">Status</a><a href="#findings">All 73 findings</a><a href="#verification">Verification</a><a href="#native">macOS / iOS acceptance</a><a href="audit.html">Original audit</a></nav><footer>Updated {UPDATED}<br>Development branch handoff<br>{goal_label}</footer></aside><main>
<header id="overview"><p class="eyebrow">Desktop · extensions · iPhone · iPad</p><h1>Findings into <em>fixes.</em></h1><p class="lead">All 73 findings are tracked below. Native compilation, actual Apple CI acceptance and several simulator scenarios have passed. M02, M04, R13 and R14 are verified after native scope, import, fault recovery/nonreplay, media/memory and asset-race checks plus clean builds; six findings retain required physical-device acceptance. Current instructions limit device execution to simulators; required physical-device acceptance remains deferred. Validated fixes are pushed to dev through 9ec4bcf2, including native script ownership and final clean integration. No release is being published.</p>
<div class="metrics"><div class="metric"><strong>73</strong><span>Findings in scope</span></div><div class="metric"><strong>{counts['verified']}</strong><span>Implemented and locally verified</span></div><div class="metric"><strong>{counts['native-pending']}</strong><span>Native acceptance pending</span></div><div class="metric"><strong>{counts['verification-pending']}</strong><span>Other verification pending</span></div></div></header>
<section><h2>What to review first</h2><div class="start-grid"><article class="start-card"><h3>Data and privacy</h3><p>Atomic saves, protected project creation, isolated pane buffers, checked conflict recovery, project-scoped AI state and export exclusions.</p></article><article class="start-card"><h3>Existing workflows</h3><p>Research persistence, imports, inline actions, speech cancellation, image loading, keyboard focus and extension teardown.</p></article></div><p class="note">{goal_note} All nine Mac verification checks and six portable temporary-access tests passed. Compilation and a simulator launch do not close the remaining acceptance cases; see the <a href="macos-validation/native-acceptance.json">native case ledger</a>. M05 corrects the original audit: a simulator job already existed; shared-code triggers were missing. Historical Aislop results below come from elevated scans after an access problem invalidated earlier unelevated results.</p></section>
<section id="findings"><h2>Per-finding ledger</h2><div class="controls"><div class="filters"><label class="search">Search<input id="search" type="search" placeholder="Finding ID, behavior, file or test"></label><label>Status<select id="status"><option value="">All</option><option value="verified">Locally verified</option><option value="native-pending">Native pending</option><option value="verification-pending">Other verification pending</option></select></label></div><div class="actions"><button id="expand">Expand visible</button><button id="collapse">Collapse</button><button id="print">Print / PDF</button><a href="implementation-progress.json" download>Download ledger</a></div></div><p id="result-count" role="status" aria-live="polite">73 findings</p>{''.join(cards)}</section>
<section id="verification"><h2>Verification and limits</h2><p>Core and Backend suites reached 100% line coverage. SDK, extension, AI, worker and renderer checks are listed with their workstream results. Existing nonfixable Aislop maintainability warnings are retained in the scan artifacts; errors and fixable warnings must be resolved before completion.</p><div class="table-wrap"><table><thead><tr><th>Check</th><th>Status</th><th>Result</th></tr></thead><tbody>{checks_html}</tbody></table></div><p>No paid model requests, real model downloads, GPU inference or live publishing were used. Four existing AI hardware tests are skipped. The 16 MiB mobile preview limit bounds base64 allocations; oversized files retain their project data and show a preview error.</p></section>
{native_handoff}</main>
<script>const rows=[...document.querySelectorAll('.finding')];const search=document.querySelector('#search');const status=document.querySelector('#status');function filter(){{const query=search.value.trim().toLowerCase();let count=0;for(const row of rows){{row.hidden=!!((status.value&&row.dataset.status!==status.value)||(query&&!row.textContent.toLowerCase().includes(query)));if(!row.hidden)count++;}}document.querySelector('#result-count').textContent=count+' findings';}}search.addEventListener('input',filter);status.addEventListener('change',filter);document.querySelector('#expand').onclick=()=>rows.filter(row=>!row.hidden).forEach(row=>row.open=true);document.querySelector('#collapse').onclick=()=>rows.forEach(row=>row.open=false);document.querySelector('#print').onclick=()=>window.print();let before=[];window.addEventListener('beforeprint',()=>{{before=rows.map(row=>row.open);rows.filter(row=>!row.hidden).forEach(row=>row.open=true)}});window.addEventListener('afterprint',()=>rows.forEach((row,index)=>row.open=before[index]));function reveal(){{const row=document.getElementById(location.hash.slice(1));if(row?.classList.contains('finding')){{search.value='';status.value='';filter();row.open=true;row.scrollIntoView();}}}}window.addEventListener('hashchange',reveal);reveal();</script></html>'''
    runtime_checks = '''<section id="runtime"><h2>Additional runtime evidence</h2><ul>
<li><strong>R17, verified:</strong> Both strengthened image-insertion scenarios pass on Windows and native Linux Electron under Debian WSLg. Normal and page view open the real context menu, hold the OS picker result, rebuild prose and clear selection before insertion. The isolated Linux harness uses <code>--no-sandbox</code> because WSL runs as root; this is not an application setting. See <a href="linux-validation/summary.json">Linux runtime evidence</a>.</li>
<li><strong>EXT-014–016, verified:</strong> Named contract tests prove fatal errors settle requests while the worker pipe remains open, missing or partial model weights report preparation required, and cancellation after the first passage prevents later generation and permits the next request. A real Python subprocess also returned a fatal error and completion within 109 ms and accepted the next request. Offline-cache restart denies socket connections. See <a href="speech-acceptance-evidence.json">speech acceptance evidence</a>. GPU inference remains an optional smoke check; it is not required by these original acceptance criteria.</li>
<li><strong>EXT-018, verified:</strong> Pen &amp; Paper loads through the native v1.7 and last native v1.14.5 host loaders. Qwen loads through its untagged 1.15-dev host (commit <code>8ffe1bf1</code>), including speech-provider registration and removal. Modern hosts reject the legacy manifests. These are loader-contract checks; native extension UI and model inference were not exercised, and the bounds do not certify every intervening historical release. See <a href="legacy-historical-validation/summary.json">historical host evidence</a>.</li>
</ul><p>Formats, Insight, Publish and Toolkit now require the next host, 3.5.4 or newer, because their corrected paths use new SDK members. Extension packaging is pinned to the future <code>v3.5.4</code> host tag and must wait for that host release. AI Assistant independently builds and loads against the clean, already released 3.5.3 SDK contract.</p><p>The legacy PnP and Qwen dev branches contain compatibility metadata and documentation only. Their pre-existing implementation work remains local. Qwen loader evidence used the preserved, unpublished 0.4 fixture; the pushed metadata retains version 0.3. Historical source links may therefore require that original fixture. See <a href="publication-extensions.json">repository delivery details</a>.</p></section>'''
    content = content.replace('<section id="native">', performance_html() + runtime_checks + '<section id="native">')
    ledger_json = json.dumps(progress, ensure_ascii=False).replace('<', '\\u003c')
    offline_download = '<script id="ledger" type="application/json">' + ledger_json + '''</script><script>
document.querySelector('a[download]').addEventListener('click', event => {
  event.preventDefault();
  const url = URL.createObjectURL(new Blob([document.querySelector('#ledger').textContent], {type:'application/json'}));
  const link = document.createElement('a'); link.href=url; link.download='novalist-implementation-progress.json'; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});</script>'''
    content = content.replace('</html>', offline_download + '</html>')
    (HERE / "implementation.html").write_text(content, encoding="utf-8")
    original = HERE / 'audit.html'
    audit_html = original.read_text(encoding='utf-8')
    if 'id="implementation-link"' not in audit_html:
        opening = '<main id="main">'
        if opening not in audit_html:
            raise ValueError('The original audit main element is missing.')
        audit_html = audit_html.replace(opening, opening + '<p class="note" id="implementation-link">Implementation is tracked in the <a href="implementation.html">73-finding implementation report</a>. This audit preserves the original pre-fix evidence.</p>', 1)
        original.write_text(audit_html, encoding='utf-8')
    print(json.dumps({"findings": len(cards), "status": counts}, indent=2))


if __name__ == "__main__":
    main()
