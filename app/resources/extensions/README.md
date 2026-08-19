# Bundled extensions

Extensions dropped in here are packaged into the installer and copied into the
writer's extensions folder the first time the app runs, by
`ExtensionLoader.SeedBundled`. One folder per extension, each holding the same
`extension.json` and assemblies the gallery would install.

Empty in a checkout. The extensions are built from their own repositories
(`novalist-extension`, `novalist-aiassistant`), and the release workflow stages
the ones a build ships here before calling electron-builder. `npm run package`
locally therefore produces an app with no bundled extensions, which is correct:
a developer's build should not carry whatever happened to be on their disk.

An extension already installed at the same version or newer is left alone, so a
writer who updates one from the gallery keeps their update.
