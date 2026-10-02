# Changelog rules

When changing desktop behavior or editing `CHANGELOG.md`, follow the changelog guidance in
[CLAUDE.md](CLAUDE.md#keep-changelogmd-focused-on-changes-since-the-last-release).

- **Unreleased** contains only user-facing desktop changes that have not already shipped.
  Check desktop release tags and published changelog sections before adding or moving entries.
- Describe a new feature's final capabilities once under **Added**. Update that entry as the
  feature develops; omit separate fixes, changes, and removals for its unreleased iterations.
- **Fixed** is for bugs users could encounter in a previously released version. A fix to
  existing released behavior still belongs even when discovered while building a new feature.
- Omit internal refactors, tests, documentation, CI/build/signing details without a user-visible
  outcome, and mobile-only work. Remove duplicates and empty category headings.
- Keep the relevant **Unreleased** entry accurate in the same change. Leave published release
  sections unchanged unless the user explicitly asks to correct release history.
