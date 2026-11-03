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

# E2E desktop isolation

Electron E2E tests activate real windows and enter native fullscreen. Do not run
them on the user's interactive desktop while they are working. Use the isolated
GitHub Actions runners or a dedicated desktop session instead.

Playwright execution requires `NOVALIST_E2E_ALLOW_DESKTOP=1`; the CI E2E steps set
it on their isolated runners. Never set this flag locally merely to make a check
pass. A local interactive run needs the user's explicit consent for that run.
`CI=true` does not mean the desktop is isolated. Listing tests with `--list`,
typechecking, and Node tests are safe without opting in.
