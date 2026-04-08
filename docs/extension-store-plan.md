# Novalist Extension Store — Implementation Plan

## Overview

An in-app extension gallery allowing users to browse, search, install, update, and remove community extensions — modeled after Obsidian's community plugins system. The gallery index is a JSON file hosted in a public GitHub repository. Each extension links to its own GitHub repository where releases are published as ZIP assets.

---

## 1. Gallery Repository (`novalist-extension-gallery`)

### 1.1 Repository structure

```
novalist-extension-gallery/
├── README.md                   # Contribution guide for extension authors
├── gallery.json                # The gallery index (array of entries)
└── CONTRIBUTING.md             # Submission review checklist
```

### 1.2 `gallery.json` schema

An array of lightweight descriptors — one per extension. This is the **only** file Novalist fetches to populate the store.

```jsonc
[
  {
    // Must match the extension's extension.json "id" field (reverse-domain)
    "id": "com.example.myextension",

    // Human-readable display name
    "name": "My Extension",

    // Short description shown in the store browse list
    "description": "Does something useful for writers.",

    // Extension author
    "author": "Jane Doe",

    // GitHub repository in "owner/repo" format — used to fetch releases & README
    "repo": "janedoe/novalist-my-extension",

    // Optional tags for filtering/search (should align with extension.json tags)
    "tags": ["productivity", "writing"],

    // Optional icon URL (displayed in browse list and detail view; placeholder used if omitted)
    "icon": "https://raw.githubusercontent.com/janedoe/novalist-my-extension/main/icon.png"
  }
]
```

**Rules:**
- `id` must be globally unique and match the extension's `extension.json` `id`.
- `repo` must be a public GitHub repository.
- Submissions are pull requests reviewed before merging (like Obsidian's plugin review).

---

## 2. Extension Release Convention

Extension authors publish releases on their GitHub repository. The store fetches releases via the GitHub API.

### 2.1 Release tag format

Tags must follow semantic versioning: `v1.0.0`, `v1.2.3`, etc.

### 2.2 Release asset requirements

Each release **must** contain exactly one ZIP asset with the naming convention:

```
{extension-id}.zip
```

For example: `com.example.myextension.zip`

### 2.3 ZIP contents

The ZIP must contain the extension folder contents at the root level (no nested wrapper folder):

```
com.example.myextension.zip
├── extension.json          # Required — manifest with id, version, minHostVersion, etc.
├── MyExtension.dll         # Required — the entry assembly
├── README.md               # Optional — overrides the repo-level README if present
├── Locales/                # Optional
│   ├── en.json
│   └── de.json
└── Themes/                 # Optional
    └── MyTheme.axaml
```

### 2.4 Version compatibility

The `extension.json` inside the ZIP must include:
- `version` — the extension version (must match the release tag without the `v` prefix).
- `minHostVersion` — minimum Novalist version required.
- `maxHostVersion` — (optional) maximum Novalist version supported. Empty string = no upper bound.

The store uses these fields to determine whether a release is compatible with the user's installed Novalist version before offering install/update.

### 2.5 Pre-release support

GitHub releases marked as `prerelease: true` are **ignored** by default. A future setting could opt users into pre-release channels.

---

## 3. Core Service Layer (`Novalist.Core`)

### 3.1 New models — `Novalist.Core/Models/ExtensionGallery.cs`

```csharp
/// A single entry from the gallery index (gallery.json).
public sealed class GalleryEntry
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Repo { get; set; }           // "owner/repo"
    public List<string> Tags { get; set; }
    public string? Icon { get; set; }           // Optional icon URL; placeholder if null
}

/// Represents a fetched release from a GitHub repo.
public sealed class GalleryRelease
{
    public string TagName { get; set; }        // e.g. "v1.2.0"
    public string Version { get; set; }        // Parsed: "1.2.0"
    public string Body { get; set; }           // Release notes (markdown)
    public bool IsPrerelease { get; set; }
    public string ZipDownloadUrl { get; set; } // browser_download_url of the ZIP asset
    public long ZipSize { get; set; }
    public DateTime PublishedAt { get; set; }
}

/// Full details for displaying an extension in the store detail view.
public sealed class GalleryExtensionDetail
{
    public GalleryEntry Entry { get; set; }
    public GalleryRelease? LatestRelease { get; set; }
    public string ReadmeMarkdown { get; set; }  // Fetched from the extension's GitHub repo
    public bool IsCompatible { get; set; }
    public bool IsInstalled { get; set; }
    public bool HasUpdate { get; set; }
    public string? InstalledVersion { get; set; }
}
```

### 3.2 New service — `Novalist.Core/Services/ExtensionGalleryService.cs`

Handles all HTTP communication with GitHub.

```
IExtensionGalleryService
├── Task<List<GalleryEntry>> FetchGalleryIndexAsync(ct)
│     → GET raw.githubusercontent.com/Drommedhar/novalist-extension-gallery/main/gallery.json
│
├── Task<List<GalleryRelease>> FetchReleasesAsync(repo, ct)
│     → GET api.github.com/repos/{owner}/{repo}/releases
│     → Filters out prereleases, finds the ZIP asset matching {id}.zip
│
├── Task<GalleryRelease?> GetLatestCompatibleReleaseAsync(entry, ct)
│     → Fetches releases, downloads extension.json from the ZIP (or from the repo),
│       checks minHostVersion/maxHostVersion against VersionInfo.Version
│
├── Task<string> FetchReadmeAsync(repo, ct)
│     → GET api.github.com/repos/{owner}/{repo}/readme (Accept: application/vnd.github.raw)
│
├── Task<string> DownloadExtensionZipAsync(release, progress, ct)
│     → Downloads ZIP to a temp directory, returns the path
│
├── Task InstallExtensionAsync(zipPath, extensionId, ct)
│     → Extracts ZIP to %APPDATA%/Novalist/Extensions/{extensionId}/
│     → Validates extension.json exists and id matches
│     → Auto-installs missing dependencies from the gallery
│
├── Task UninstallExtensionAsync(extensionId, ct)
│     → Deletes %APPDATA%/Novalist/Extensions/{extensionId}/
│
├── Task<List<ExtensionUpdateInfo>> CheckForUpdatesAsync(installedExtensions, ct)
│     → For each installed extension that has a matching gallery entry,
│       checks if a newer compatible release exists
│
└── string? GitHubToken { get; set; }
      → Optional PAT added to Authorization header to avoid rate limits
```

**HTTP Client details:**
- Single static `HttpClient` with `User-Agent: Novalist-ExtensionStore`.
- If `GitHubToken` is set, adds `Authorization: Bearer {token}` header.
- GitHub API rate limit: 60/hr unauthenticated, 5000/hr with PAT.
- Responses are cached in-memory for the session (gallery index, READMEs).

### 3.3 Compatibility checking logic

```
For each release (newest first):
  1. Download/parse the extension.json from the release ZIP or repo tag
  2. Compare minHostVersion against VersionInfo.Version  → host must be >=
  3. Compare maxHostVersion against VersionInfo.Version  → host must be <= (if set)
  4. First compatible release wins → that's the "latest compatible"
```

**Optimization:** To avoid downloading the full ZIP just for compatibility checking, extension authors should also include `minHostVersion` and `maxHostVersion` in their release body (structured footer) or we can fetch the raw `extension.json` from the tagged commit:
```
GET https://raw.githubusercontent.com/{owner}/{repo}/{tag}/extension.json
```

---

## 4. Settings & Persistence

### 4.1 New fields in `AppSettings`

```csharp
[JsonPropertyName("galleryLastChecked")]
public DateTime? GalleryLastChecked { get; set; }

[JsonPropertyName("githubToken")]
public string? GitHubToken { get; set; }          // Optional PAT (stored securely)

[JsonPropertyName("extensionAutoUpdate")]
public bool ExtensionAutoUpdate { get; set; }      // Future: auto-install updates
```

### 4.2 Installed extension metadata

Store a small `store-meta.json` alongside each installed extension to track gallery origin:

```
%APPDATA%/Novalist/Extensions/{extensionId}/
├── extension.json        # Standard manifest
├── store-meta.json       # Gallery tracking metadata
├── *.dll
└── ...
```

```jsonc
// store-meta.json
{
  "installedFromGallery": true,
  "repo": "janedoe/novalist-my-extension",
  "installedVersion": "1.2.0",
  "installedAt": "2026-04-08T12:00:00Z"
}
```

This lets us distinguish gallery-installed vs manually-installed extensions and know where to check for updates.

---

## 5. Desktop UI (`Novalist.Desktop`)

### 5.1 Enhanced Extensions View

The existing `ExtensionsView` gets two tabs:

```
┌─────────────────────────────────────────────────────────┐
│  [Installed]  [Browse Store]                            │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  (tab content changes based on selection)               │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 5.2 "Installed" tab (enhanced current view)

The existing list of installed extensions, enhanced with:
- **Update badge** — shown if a newer compatible version is available in the gallery.
- **Update button** — one-click update (download + replace files + reload).
- **Uninstall button** — deletes the extension folder (with confirmation).
- **"Manually installed"** label for extensions not from the gallery.

### 5.3 "Browse Store" tab

```
┌───────────────────────────────────────────────────────────┐
│ 🔍 [Search extensions...        ]  [Filter ▾: All Tags]  │
├───────────────────────────────────────────────────────────┤
│                                                           │
│  ┌─────────────────────────────────────────────────────┐  │
│  │ 📦 My Extension                          v1.2.0    │  │
│  │ Jane Doe                                           │  │
│  │ Does something useful for writers.                 │  │
│  │ [productivity] [writing]            [Install]      │  │
│  └─────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌─────────────────────────────────────────────────────┐  │
│  │ 📦 Another Extension                     v2.0.1    │  │
│  │ John Smith                                         │  │
│  │ Another great tool.                                │  │
│  │ [ai] [analysis]                     [Installed ✓]  │  │
│  └─────────────────────────────────────────────────────┘  │
│                                                           │
└───────────────────────────────────────────────────────────┘
```

**Features:**
- Search by name, description, author, tags.
- Tag filter dropdown (union of all tags in the gallery).
- Each card shows: name, author, description, tags, latest compatible version.
- Button states: `Install` / `Installed ✓` / `Update available` / `Incompatible`.

### 5.4 Extension detail view

Clicking an extension card opens a detail panel (or replaces the list):

```
┌───────────────────────────────────────────────────────────┐
│ [← Back]                                                  │
│                                                           │
│ My Extension  v1.2.0                                      │
│ by Jane Doe                                               │
│ ─────────────────────────────────────────────────────────  │
│ [Install]  or  [Uninstall] [Update to v1.3.0]            │
│                                                           │
│ ─────────────────────────────────────────────────────────  │
│ README.md content rendered here (from the GitHub repo)    │
│                                                           │
│ Supports Markdown: headings, lists, code blocks, images,  │
│ bold, italic, links, etc.                                 │
│                                                           │
│ ─────────────────────────────────────────────────────────  │
│ Release Notes (v1.2.0):                                   │
│ - Fixed bug with ...                                      │
│ - Added support for ...                                   │
│                                                           │
│ ─────────────────────────────────────────────────────────  │
│ Details:                                                   │
│ Author: Jane Doe                                          │
│ Tags: productivity, writing                               │
│ Min Novalist Version: 0.5.0                               │
│ Repository: github.com/janedoe/novalist-my-extension      │
│                                                           │
└───────────────────────────────────────────────────────────┘
```

### 5.5 Settings integration

Add a section in preferences/settings for the extension store:
- **GitHub Personal Access Token** — optional text field (password-masked) to increase API rate limit.
- **Check for extension updates on startup** — toggle (default: on).

---

## 6. Startup Update Check

On app startup (background, non-blocking):

1. Load installed extensions normally (existing flow).
2. If `GalleryLastChecked` is older than 24 hours (or on first launch):
   a. Fetch `gallery.json`.
   b. For each installed extension that has a `store-meta.json`:
      - Fetch the latest compatible release from its repo.
      - Compare with `installedVersion`.
   c. Store results in memory.
   d. Update `GalleryLastChecked`.
3. If updates are available, show a non-intrusive notification: *"N extension update(s) available"*.
4. Clicking the notification navigates to the Installed tab with update badges visible.

---

## 7. Implementation Phases

### Phase 1 — Foundation
- [x] Create `gallery.json` schema and seed the gallery repo with 0–2 test entries.
- [x] Implement `GalleryEntry`, `GalleryRelease`, `GalleryExtensionDetail` models.
- [x] Implement `ExtensionGalleryService` (fetch index, fetch releases, fetch README, download ZIP).
- [x] Implement ZIP extraction + install to `%APPDATA%/Novalist/Extensions/{id}/`.
- [x] Implement `store-meta.json` writing on install.
- [x] Implement uninstall (delete folder).

### Phase 2 — Store UI (Browse)
- [x] Add tab control to `ExtensionsView` (Installed / Browse Store).
- [x] Create `ExtensionStoreViewModel` with search, filter, and gallery list.
- [x] Create browse list UI with extension cards.
- [x] Create detail view with README rendering and install/uninstall buttons.
- [x] Wire install/uninstall actions with progress indication.
- [x] Implement automatic dependency resolution (auto-install missing dependencies from gallery).

### Phase 3 — Updates
- [x] Implement update checking logic (compare installed vs latest compatible).
- [x] Add update badges and one-click update button to Installed tab.
- [x] Add startup background update check with notification.
- [x] Add `AppSettings` fields for gallery preferences.

### Phase 4 — Polish
- [x] Add GitHub PAT configuration in settings.
- [x] Add in-memory caching for gallery data (avoid redundant requests).
- [x] Add error handling / retry for network failures (offline graceful degradation).
- [x] Add rate limit detection and user-friendly messaging.
- [x] Localize all new UI strings.
- [x] Document the release convention in the gallery repo's README / CONTRIBUTING.md.
- [x] Document the process for extension authors in `docs/extension-guide.md`.

---

## 8. Security Considerations

| Concern | Mitigation |
|---------|------------|
| Malicious extensions | Gallery entries are PR-reviewed before merging (gatekeeper model). |
| ZIP path traversal | Validate all extracted paths are within the target directory. Reject entries with `..` segments. |
| GitHub token exposure | Token stored in app settings, never logged. Transmitted only to `api.github.com` over HTTPS. |
| DLL loading | Existing `ExtensionLoader` already loads from a sandboxed directory. No change needed. |
| Rate limiting | Optional PAT support + 24h check interval + in-memory caching mitigate hitting limits. |

---

## 9. Resolved Decisions

| Question | Decision |
|----------|----------|
| Extension signing | Not needed. Gallery PR review is the trust gate. |
| Extension screenshots | No separate images array. Screenshots live in the extension repo's README, which is rendered in the detail view. |
| Categories vs tags | Free-form tags only. No enforced categories. |
| Extension icon | Optional `icon` URL field in `gallery.json`. Placeholder icon used when omitted. |
| Dependency resolution | The store will auto-install required dependencies when installing an extension. |
