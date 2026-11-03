# PDF dependency replacement

**Goal:** Resolve Dependabot #40 without requiring a license key, preserving desktop and iOS PDF exports.

**Approach:** Replace PdfSharpCore and ImageSharp with the MIT-licensed PDFsharp 6.2.4 and SkiaSharp 4.154.0. Keep the existing page layout code. Decode images through SkiaSharp and supply PNG/JPEG streams to PDFsharp; resolve installed fonts through SkiaSharp. ImageSharp 4 was rejected because Release builds require a license key, which the user does not have. The user authorized replacing the dependencies.

**Constraints:** Preserve covers, manuscript images, Codex portraits, transparency, font styles, page boxes, bookmarks, and corrupt-image fallback. Keep the 100% coverage gate. Leave published changelog sections unchanged. Include native runtime assets for desktop and iOS.

- [x] Reproduce missing PDF images with actual output assertions for PNG/JPEG covers, scenes and Codex portraits.
- [x] Replace package references and migrate PDF API names in services and tests.
- [x] Implement image decoding and installed-font resolution with regression tests.
- [ ] Verify export tests, full .NET suites and coverage, dependency audit, Release builds and platform publication.
- [ ] Update the security changelog entry and PR description, review the final diff, run CI, and merge both requested PRs.

**Review focus:** Font fallback and bold/italic faces; transparent PNGs and WebP/GIF input; corrupt or missing files; native asset delivery on Linux/macOS/Windows/iOS; no license key required for Release builds.

**Validation before CI:** 407 export/font tests pass. Full local suite: SDK 61 and Backend 2,086 pass; Core 3,988 pass with three existing macOS file-lock failures. Core, SDK and Backend each retain 100% line coverage in their own test reports. Release solution build, self-contained macOS publish and iOS dependency restore pass. Official NuGet advisory data finds no vulnerable package in the replacement Core graph. The final independent review found portrait sizing differed; a PNG/JPEG output regression now pins the previous 96-DPI layout.
