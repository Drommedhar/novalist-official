import html
import json
import subprocess
from collections import Counter
from pathlib import Path
from urllib.parse import quote

HERE = Path(__file__).parent
ROOT = HERE.parents[1]
REPOS = ROOT.parent
PARTS = ["backend", "renderer", "extensions", "root", "supplemental-root", "supplemental-mobile"]
SNAPSHOTS = {}


def esc(value):
    return html.escape(str(value), quote=True)


def anchor(identifier):
    return '<a href="#{}">{}</a>'.format(esc(identifier), esc(identifier))


def git_value(repo, *args):
    directory = REPOS / repo
    return subprocess.check_output(
        ["git", "-c", f"safe.directory={directory.as_posix()}", "-C", str(directory), *args],
        text=True, encoding="utf-8").strip()


def snapshot(repo):
    if repo not in SNAPSHOTS:
        SNAPSHOTS[repo] = {"sha": git_value(repo, "rev-parse", "HEAD"),
                           "remote": git_value(repo, "remote", "get-url", "origin").removesuffix(".git")}
    return SNAPSHOTS[repo]


def source_location(finding, evidence):
    path = Path(evidence["path"])
    if not path.is_absolute():
        owner = finding["repo"].split(" + ")[0]
        path = REPOS / owner / path
    path = path.resolve()
    relative = path.relative_to(REPOS)
    repo = relative.parts[0]
    repo_path = Path(*relative.parts[1:]).as_posix()
    if not path.is_file():
        raise ValueError(f"Missing evidence: {path}")
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    line = evidence["line"]
    if not 1 <= line <= len(lines):
        raise ValueError(f"Evidence line out of bounds: {path}:{line} ({len(lines)})")
    info = snapshot(repo)
    remote = info["remote"].replace("git@github.com:", "https://github.com/")
    return f"{repo}/{repo_path}:{line}", f"{remote}/blob/{info['sha']}/{quote(repo_path)}#L{line}"


def component(finding):
    identifier = finding["id"]
    if identifier.startswith("M"):
        return "Mobile native host"
    if identifier.startswith("A") or identifier == "P02":
        return "AI Assistant"
    if identifier.startswith("BE") or identifier.startswith("H"):
        return "Core, backend & SDK"
    if identifier == "EXT-018":
        return "Legacy extensions"
    if identifier.startswith("EXT"):
        return "Active extensions"
    if identifier.startswith("G"):
        return "Extension gallery"
    return "Renderer & editor"


def platforms(finding):
    declared = finding.get("platforms")
    if declared:
        return declared
    if finding["id"].startswith("BE"):
        return ["Desktop"] if finding["id"] == "BE-04" else ["Desktop", "iOS/iPadOS (shared Core)"]
    return ["Desktop"]


def load_data():
    findings, checks, coverage = [], [], []
    for part in PARTS:
        data = json.loads((HERE / f"{part}.json").read_text(encoding="utf-8-sig"))
        findings.extend(data["findings"])
        checks.extend(data.get("checks", []))
        coverage.extend(data.get("coverage", []))
    for finding in findings:
        finding["component"] = component(finding)
        finding["platforms"] = platforms(finding)
        if finding["id"] in {"R06", "R07"}:
            finding["platforms"] = ["Desktop", "iPadOS (Research UI)"]
        for evidence in finding["evidence"]:
            evidence["label"], evidence["url"] = source_location(finding, evidence)
    if len({f["id"] for f in findings}) != len(findings):
        raise ValueError("Duplicate finding IDs")
    next(f for f in findings if f["id"] == "BE-04")["title"] = "Desktop saves overwrite the current file in place"
    checks.append({"command": "dotnet test tests/Novalist.Sdk.Tests/Novalist.Sdk.Tests.csproj --no-restore -m:1 -nr:false",
                   "status": "passed", "detail": "59 SDK tests passed against the current source."})
    checks.append({"command": "node artifacts/audit-2026-10-06/verify-report.cjs",
                   "status": "passed", "detail": "Headless Chromium verified finding counts, filters, search, expansion, filtered JSON download, deep links, internal anchors, print expansion/restoration and 390px layout. No JavaScript errors, horizontal overflow or network dependencies."})
    coverage = [
        "Current-source Core, Backend, SDK, extension and AI test suites ran on Windows; transport and Speech Python tests also passed. Four AI tests were skipped.",
        "Isolated audit probes use synthetic fixtures and current source; the report specifies which behavior was reproduced and which was traced statically.",
        "No full installed-application Electron e2e sweep, native macOS/iOS build, simulator or physical-device execution was performed. The mobile web bundle was built successfully.",
        "Shared Core findings identify mobile code applicability; they do not establish that every desktop UI entry point is exposed on mobile.",
        "Publishing attribute injection was reproduced in a source-linked generator fixture and its generated event-handler form executed in headless Chromium. No real site was published.",
        "No real model downloads, paid AI requests, GPU inference, live dictionary/web-capture service checks or packaged extension installation were performed.",
        "Backend NuGet and production npm dependency advisory queries reported no known vulnerabilities. This is not a full dependency freshness or security certification.",
        "Legacy Pen & Paper and Qwen3 inspection covered inventory, manifests, dependencies and native contributor compatibility only, not deep feature behavior or builds.",
        "Existing serialized workspace mutations, checked scene writes, coordinated mobile writes, ZIP checks, authenticated import requests and diagnostic redaction were considered when reviewing findings.",
        "Production source, installed extensions, user projects and changelogs were left unchanged. Audit code and logs are isolated in the report directory."
    ]
    return sorted(findings, key=lambda f: (f["priority"], f["id"])), checks, coverage


def render_evidence(finding):
    rows = []
    for evidence in finding["evidence"]:
        rows.append(f'<li><a href="{esc(evidence["url"])}" target="_blank" rel="noopener noreferrer">{esc(evidence["label"])}</a><p>{esc(evidence["detail"])}</p></li>')
    return '<div class="evidence"><h4>Code evidence at the audited revision</h4><ul>' + ''.join(rows) + '</ul></div>'


def render_finding(finding, rank):
    confidence = {"reproduced": "Reproduced", "confirmed-code": "Code-confirmed", "hypothesis": "Needs measurement"}[finding["confidence"]]
    platform_key = ' '.join(key for key, test in [("desktop", "desktop"), ("mobile", "ios")] if any(test in p.lower() for p in finding["platforms"]))
    if any("ipad" in p.lower() for p in finding["platforms"]):
        platform_key += " mobile"
    metadata = [finding["component"], finding["category"], ' / '.join(finding["platforms"]), "Effort " + finding["effort"]]
    body = ''.join(f'<dt>{label}</dt><dd>{esc(finding[field])}</dd>' for label, field in [
        ("Trigger", "trigger"), ("Impact", "impact"), ("Improve", "recommendation"), ("Verify", "validation")])
    source_links = ' '.join(f'<a href="{esc(source["url"])}" target="_blank" rel="noopener noreferrer">{esc(source["title"])}</a>' for source in finding.get("sources", []))
    return f'''<details class="finding" id="{esc(finding['id'])}" data-priority="{finding['priority']}" data-area="{esc(finding['component'])}" data-platform="{platform_key}" data-evidence="{finding['confidence']}" data-effort="{finding['effort']}" data-rank="{rank:03d}">
    <summary><div class="summary-top"><span class="badge {finding['priority'].lower()}">{finding['priority']}</span><span class="identifier">{esc(finding['id'])}</span><span>{confidence}</span><span class="expand-mark" aria-hidden="true">+</span></div>
    <h3>{esc(finding['title'])}</h3><div class="metadata">{''.join(f'<span>{esc(item)}</span>' for item in metadata)}</div></summary>
    <div class="finding-body"><dl>{body}</dl>{render_evidence(finding)}
    <div class="finding-footer"><a href="#{finding['id']}">Link to {finding['id']}</a><span>{source_links}</span></div></div></details>'''


def option_list(values):
    return ''.join(f'<option value="{esc(value)}">{esc(value)}</option>' for value in values)


def render_backlog(findings):
    areas = sorted({finding["component"] for finding in findings})
    return f'''<section id="backlog"><div class="section-head"><h2>The improvement backlog</h2><p>Every item changes an existing capability. Open a row for its trigger, impact, bounded fix and acceptance test.</p></div>
    <div class="legend"><p><span class="badge p1">P1</span> Protect data, privacy or a blocked workflow</p><p><span class="badge p2">P2</span> Correctness and reliability</p><p><span class="badge p3">P3</span> Polish and maintenance</p></div>
    <p class="small">Effort estimates: S = usually up to half a day; M = roughly half a day to two days; L = several days. Includes a focused regression check, excludes release/device queues. Estimates need confirmation before implementation.</p>
    <div class="controls"><div class="filters">
    <label class="search" for="search">Search findings, code paths or IDs <input type="search" id="search" placeholder="Try: autosave, speech, privacy, iOS, A12..." autocomplete="off"></label>
    <label for="priority">Priority<select id="priority"><option value="">All priorities</option>{option_list(['P1', 'P2', 'P3'])}</select></label>
    <label for="area">Area<select id="area"><option value="">All areas</option>{option_list(areas)}</select></label>
    <label for="platform">Platform<select id="platform"><option value="">All platforms</option><option value="desktop">Desktop</option><option value="mobile">iPhone / iPad / shared mobile code</option></select></label>
    <label for="evidence">Evidence<select id="evidence"><option value="">All evidence</option><option value="reproduced">Reproduced</option><option value="confirmed-code">Code-confirmed</option></select></label>
    <label for="effort">Effort<select id="effort"><option value="">All sizes</option><option value="S">S — Small</option><option value="M">M — Medium</option><option value="L">L — Larger</option></select></label>
    <label for="sort">Sort<select id="sort"><option value="priority">Priority first</option><option value="effort">Smallest effort first</option><option value="area">Area, then priority</option></select></label></div>
    <div class="preset-row"><button data-preset="urgent">P1 first</button><button data-preset="quick">Small fixes</button><button data-preset="mobile">Mobile</button><button data-preset="reproduced">Reproduced only</button><button id="reset">Reset filters</button></div></div>
    <div class="results-row"><span id="result-count" role="status" aria-live="polite">{len(findings)} findings</span><div class="actions"><button id="expand">Expand visible</button><button id="collapse">Collapse all</button><button id="download">Download visible JSON</button><button id="print">Print visible / PDF</button></div></div>
    <noscript><p>JavaScript is disabled. All findings remain available below; use your browser's Find command.</p></noscript><p id="empty" class="note" hidden>No findings match these filters. Reset filters to see the full audit.</p>
    <div id="findings">{''.join(render_finding(f, i) for i, f in enumerate(findings))}</div></section>'''


def render_priorities():
    cards = [
        ("01 / Save and recover", "Keep every version of the writer's work", "Fix independent-pane overwrites, conflict navigation, blank conflict resolution and non-atomic desktop saves as one persistence batch.", "R01", "R02", "R03", "BE-04", "BE-05"),
        ("02 / Export and publish", "Respect what the writer chose to withhold", "Use consistent scene exclusions, remove owned stale pages and escape HTML attributes before any further publishing polish.", "EXT-001", "EXT-002", "EXT-003"),
        ("03 / AI Assistant", "Make AI context and task lifetime explicit", "Stop unselected entity data entering prompts. Repair the reproduced knowledge-scan deadlock, cancellation and shared request state.", "A01", "A12", "A04", "A05"),
        ("04 / Mobile", "Protect the trip into the background", "Connect native lifecycle events to pending saves, make folder access balanced, and test asset loading through the actual WKWebView bridge.", "M01", "M02", "M04", "R13"),
    ]
    return '<section id="start"><div class="section-head"><h2>Where to start</h2><p>These batches have the clearest effect on trust in existing workflows.</p></div><div class="start-grid">' + ''.join(
        f'<article class="start-card"><div class="eyebrow">{esc(card[0])}</div><h3>{esc(card[1])}</h3><p>{esc(card[2])}</p>{" &nbsp; ".join(anchor(identifier) for identifier in card[3:])}</article>' for card in cards
    ) + '</div></section>'


def render_plan():
    return '''<section id="plan"><div class="section-head"><h2>A useful three-day starting plan</h2><p>A focused first pass, not a promise to clear the whole backlog. Stop expanding a batch if its acceptance checks are not green.</p></div>
    <div class="day-grid">
    <article class="day"><div class="eyebrow">Day 1 / Protect writing</div><h3>Save, conflict, restore</h3><ul><li>Turn the existing split/conflict probes into permanent regressions: <a href="#R01">R01</a>–<a href="#R05">R05</a>.</li><li>Land small collision and backup fixes when independently reviewable: <a href="#BE-01">BE-01</a>, <a href="#BE-02">BE-02</a>, <a href="#BE-05">BE-05</a>.</li><li>Require reopen/restore tests, not only in-memory assertions.</li></ul></article>
    <article class="day"><div class="eyebrow">Day 2 / Close disclosure gaps</div><h3>Exports, AI and extensions</h3><ul><li>Run a synthetic public/private scene through every exporter: <a href="#EXT-001">EXT-001</a>–<a href="#EXT-003">EXT-003</a>.</li><li>Repair the AI context contract and knowledge deadlock: <a href="#A01">A01</a>, <a href="#A12">A12</a>.</li><li>Preserve suggestion/alternative results across the SDK boundary: <a href="#EXT-005">EXT-005</a>.</li></ul></article>
    <article class="day"><div class="eyebrow">Day 3 / Verify on Apple platforms</div><h3>Device checks and small wins</h3><ul><li>Run the native checklist below on iPhone and iPad before treating mobile findings as runtime-verified.</li><li>Address lifecycle/bridge failures first; keep risky changes isolated.</li><li>If time remains, remove help images from the startup bundle (<a href="#P01">P01</a>) and fix measured small UI defects.</li></ul></article>
    </div><p class="small">For desktop user-visible fixes, update the appropriate Unreleased changelog entry and preserve published release sections. Mobile-only fixes stay out of the desktop changelog. No product code or changelog was changed during this audit.</p></section>'''


def render_mobile():
    tests = [
        ("Save before suspension", "Use a disposable project. Type a unique marker and immediately lock/background the app. Terminate it after suspension, reopen, and verify scene text; on iPad, also test research notes. Repeat offline and with an unresolved conflict.", "Marker survives or recovery explicitly offers it; no silent discard.", "M01, R02, R06"),
        ("Cloud files and folder grants", "Open an iCloud/third-party-provider folder containing cloud-only files. Cycle through projects, reselect the same folder, revoke access, go offline and resume. Record balanced grant starts/stops.", "No false empty project, unbalanced grant growth or overwritten inaccessible content.", "M02"),
        ("App update and project identity", "Install an updated build over a fixture with recent projects. Also create two different projects with the same /Documents/Foo suffix, one external and unavailable.", "An app-owned project relocates correctly; the external entry never changes identity.", "M03"),
        ("Images, maps and research media", "Open an image-only scene, an editor image, a map with a background, a research PDF and audio/video. Rapidly switch between two entities/projects with distinct same-path images.", "Every asset renders from the correct project; no stale response overwrites a newer image.", "R11, R13, R14"),
        ("Native bridge recovery", "Cold-launch repeatedly, cancel pickers, background during a request and simulate WebView process termination in a debug build. Inject a dropped/failed bridge delivery.", "Pending actions settle or recover visibly; reconnect never duplicates a write.", "M04"),
        ("Touch, VoiceOver and keyboard", "On iPhone and iPad, open the inspector sheet, conflict dialog, quick-open and an entity editor. Use VoiceOver, a hardware keyboard, rotation and iPad split view.", "Focus stays in a modal, returns on dismissal, selected items remain visible and the keyboard does not hide the editing target.", "R12, R15, R16"),
        ("Dictation and microphone permissions", "Deny microphone access first, then grant it through Settings. Start dictation, interrupt with a phone call/audio-route change, and background the app. Exercise English and German system speech.", "Denial explains how to grant permission; interrupted sessions stop cleanly and retain already-accepted prose.", "M06, M01"),
        ("Import, export and share", "Import a disposable Scrivener package from Files, cancel/retry, export a document with companion images, and use Save to Files/AirDrop on iPad. Cancel and repeat the share sheet.", "No picker hangs, incomplete companion files, premature export cleanup or iPad popover crash.", "M05"),
    ]
    rows = ''.join(f'<article class="native-test"><span class="number">{i:02d}</span><div><h3>{esc(title)}</h3><p>{esc(steps)}</p><p class="small"><strong>Pass condition:</strong> {esc(expected)}</p><p class="small">Related: {", ".join(anchor(key) for key in refs.split(", "))}</p></div></article>' for i, (title, steps, expected, refs) in enumerate(tests, 1))
    return f'''<section id="mobile"><div class="section-head"><h2>Take this checklist to macOS</h2><p>Native execution is still pending. The Windows audit built the mobile web assets and reviewed the C#, Swift and bridge code.</p></div>
    <div class="note">The repository currently targets iPhone/iPad with <code>net10.0-ios27.0</code>. No Android target or implementation was found; adding one is outside this audit. Match the repository's Xcode/.NET/workload pins before building. Use fixture projects, not a manuscript's only copy.</div>
    <p class="small">Minimum useful matrix: one iPhone and one iPad on the supported OS; local Documents and an external file-provider folder; simulator for deterministic native/bridge checks plus a physical device for suspension, permissions and audio. Re-run desktop shared behavior on macOS as well.</p>
    <div class="native-list">{rows}</div>
    <p class="small">Build entry: <code>npm run build:mobile</code> from <code>app/</code>, then the MAUI project using the SDK/toolchain settings in <code>Novalist.Mobile.csproj</code> and <code>.github/workflows/release.yml</code>. The signed release command is not needed for the initial simulator audit. Record device, OS, commit, fixture, result and console trace for each check.</p></section>'''


def render_coverage(checks):
    scopes = [
        ("novalist-official", "Desktop + shared Core/Backend/SDK", "Persistence, workspace changes, editing, backups, snapshots, export/import, extension contracts, security boundaries and tests."),
        ("novalist-official", "iPhone / iPad native + shared UI", "MAUI host, WKWebView bridge, native folder/picker/share paths, Apple Speech sources, mobile assets, lifecycle and build workflow. No Apple runtime execution."),
        ("novalist-aiassistant", "AI Assistant", "Context privacy, histories, providers, streaming/cancellation, knowledge, model state, release workflow and existing tests. No paid/live model requests."),
        ("novalist-extension", "Formats, Insight, Publish, Speech, Toolkit", "Code/contract tracing, current C# and Python tests, isolated adversarial probes. No GPU/model downloads or full installed-extension acceptance run."),
        ("novalist-extension-gallery", "Extension discovery metadata", "Gallery/manifest IDs and descriptive consistency; release conventions. Remote release asset availability was not audited."),
        ("novalist-pnp", "Legacy Pen & Paper — limited", "Inventory, manifest, dependencies and native contributor compatibility only. No RPG/PDF/import feature audit or build."),
        ("novalist-qwentts", "Legacy Qwen TTS — limited", "Inventory, manifest, dependencies and native contributor compatibility only. Current Speech extension audited separately; no old engine/playback audit."),
    ]
    rows = []
    for repo, scope, detail in scopes:
        info = snapshot(repo)
        rows.append(f'<tr><td><strong>{esc(scope)}</strong><br><code>{repo}</code><br><a href="{esc(info["remote"])}/commit/{info["sha"]}" target="_blank" rel="noopener noreferrer"><code>{info["sha"][:10]}</code></a></td><td>{esc(detail)}</td></tr>')
    check_rows = ''.join(f'<details class="check"><summary><span class="check-status {esc(check["status"])}">{esc(check["status"])}</span><code>{esc(check["command"])}</code></summary><p>{esc(check["detail"])}</p></details>' for check in checks)
    return f'''<section id="coverage"><div class="section-head"><h2>What was checked</h2><p>Audited working trees were clean at the start. Findings refer to the local source at the commits below.</p></div>
    <div class="table-wrap"><table><thead><tr><th scope="col">Area and source revision</th><th scope="col">Depth and limits</th></tr></thead><tbody>{''.join(rows)}</tbody></table></div>
    <p class="small">The older standalone app, private forks, actual author projects and third-party extensions outside these repositories were not reviewed. This is a targeted, evidence-based audit, not a claim that every code path has been exhaustively verified.</p>
    <h3>Validation ledger</h3><p class="small">6,532 existing tests passed across Core (3,860), Backend (2,036), SDK (59), extensions (365), AI Assistant (100), Speech Python (37) and transport (75). Four AI tests were skipped. Isolated audit probes are additional and intentionally demonstrate defects; they are not production regression tests.</p>
    {check_rows}
    <p class="small">Local artifacts: <a href="findings.json">combined findings</a>, <a href="source-checks.log">source checks</a>, <a href="aislop-desktop.json">quality scan</a>, <a href="mobile-build.log">mobile build</a>, <a href="backend-repro-results.json">backend reproductions</a>, <a href="ai-probes.log">AI reproductions</a>, <a href="knowledge-probe-results.json">knowledge deadlock</a>, <a href="npm-audit.json">npm advisories</a>, <a href="nuget-audit.log">NuGet advisories</a>.</p></section>'''


def render_method():
    return '''<section id="method"><h2>How to read the evidence</h2>
    <p><strong>Reproduced</strong> means the described defect, state transition or bundle measurement was observed in an isolated fixture using current source. It does not imply the entire installed-app workflow was exercised. Each item says what was actually run.</p>
    <p><strong>Code-confirmed</strong> means the triggering path and missing/incorrect behavior were traced through callers and relevant safeguards. Platform-specific execution and fault-injection steps are supplied for confirmation before shipping a fix.</p>
    <p class="small">Findings were reviewed across independent core, renderer and extension passes, then deduplicated. Existing safeguards were considered: serialized workspace mutations, checked scene writes, staged coordinated mobile writes, safe ZIP checks, authenticated loopback import, content-redacted diagnostics and restricted extension/audio protocols. A suspected opaque-origin project-file read was rejected by a runtime probe and was omitted from the backlog.</p>
    <p class="small">The quality scan scored 100/100 with three non-fixable warnings; Swift was outside its supported languages. Passing checks and advisory scans are useful evidence, not a correctness/security certification. No new full product feature is proposed, no production fix was applied, and no release or changelog was changed.</p>
    </section>'''


def write_report():
    findings, checks, coverage = load_data()
    count = Counter(f["priority"] for f in findings)
    reproduced = sum(f["confidence"] == "reproduced" for f in findings)
    mobile = sum(any("ios" in p.lower() or "ipad" in p.lower() for p in f["platforms"]) for f in findings)
    sections = render_priorities() + render_plan() + render_backlog(findings) + render_mobile() + render_coverage(checks) + render_method()
    data = {"title": "Novalist quality audit", "date": "2026-10-06", "findings": findings,
            "checks": checks, "coverageNotes": coverage, "snapshots": SNAPSHOTS,
            "limitations": "No native macOS/iOS/device execution. Legacy extension compatibility review only. No production fixes applied."}
    payload = json.dumps(data, ensure_ascii=False).replace("<", "\\u003c")
    report = f'''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="description" content="A source-backed audit of existing Novalist desktop, mobile and extension behavior."><title>Novalist — quality audit · 6 October 2026</title><style>{(HERE / 'report.css').read_text(encoding='utf-8')}</style></head><body>
    <a class="skip" href="#main">Skip to report</a><aside class="sidebar"><div><div class="brand">Novalist</div><div class="eyebrow">Quality audit / 2026.10</div></div><nav aria-label="Report sections"><a href="#main">Overview</a><a href="#start">Where to start</a><a href="#plan">Three-day starting plan</a><a href="#backlog">All {len(findings)} findings</a><a href="#mobile">macOS / device checklist</a><a href="#coverage">Scope & checks</a><a href="#method">Evidence & limits</a></nav><footer><p>Desktop · iPhone · iPad<br>AI Assistant · Extensions</p><p>6 October 2026<br>Local source audit</p><p>Self-contained HTML.<br>Works offline. Code links open the audited revision.</p></footer></aside>
    <main id="main"><header><div class="hero-top"><span class="eyebrow">Existing capabilities. Better foundations.</span><span class="issue-label">Audit 01 / 6 Oct 2026</span></div><h1>Make the writing<br><em>safe to keep.</em></h1><p class="lead">{len(findings)} concrete improvements across Novalist desktop, mobile and its extensions. Start with data protection, then correctness, reliability, performance and everyday usability.</p>
    <div class="metrics"><div class="metric"><strong>{len(findings)}</strong><span>Prioritized findings</span></div><div class="metric"><strong>{count['P1']}</strong><span>P1 · highest priority</span></div><div class="metric"><strong>{reproduced}</strong><span>Reproduced findings</span></div><div class="metric"><strong>{mobile}</strong><span>Mobile or shared-code findings</span></div></div>
    <p class="small">{count['P1']} P1 / {count['P2']} P2 / {count['P3']} P3 · 6,532 existing tests passed · Mobile web build passed · Native Apple execution pending</p>
    <p class="note">This report is an improvement backlog for capabilities that already exist. It includes triggers, source references, scoped fixes and verification steps. Native mobile findings are code-reviewed and come with a device checklist; they are not represented as on-device reproductions.</p></header>
    {sections}<footer class="foot">Prepared from local repositories on 6 October 2026. HTML, findings JSON and reproduction sources are retained in <code>artifacts/audit-2026-10-06/</code>. Production working trees were left unchanged.</footer></main>
    <script type="application/json" id="audit-data">{payload}</script><script>{(HERE / 'report.js').read_text(encoding='utf-8')}</script></body></html>'''
    (HERE / "audit.html").write_text(report, encoding="utf-8")
    (HERE / "findings.json").write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"findings": len(findings), "priorities": count, "reproduced": reproduced,
                      "mobile": mobile, "htmlBytes": len(report.encode('utf-8')), "checks": len(checks)}))


if __name__ == "__main__":
    write_report()
