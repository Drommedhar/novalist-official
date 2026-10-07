# R15 simulator evidence

The original physical-device acceptance cases remain pending. These records support the simulator investigation and regression only.

- Root files: unchanged production revision `29cefb83` baseline, native key-delivery observations, and real nested-palette inert failure. `before-summary.json` records the limits.
- `inert-only/`: first candidate; native palette accessibility improved, but delayed DOM focus after dismissal did not return to the invoking input.
- `final/`: historical filename retained for the next, superseded candidate. Exact source and bundle manifests distinguish it from the accepted source. It adds mount-time focus refresh and prevents pointerdown focus loss, but still closes the palette during pointerdown. Its native trace shows removal before pointerup; the clean landscape Context-tab observation is not a proven native click-through trace. `intermediate-test-classification.json` distinguishes product observations from unsupported input and test expectations.
- `release-dismissal/`: accepted simulator regression, retaining the palette through pointerdown and closing it on click. Source, asset, clean-build, browser and native evidence are attributed here. The top-level summary records the completed native matrix and remaining physical limits.

No synthetic KeyboardEvent or scripted UI state was used. The temporary observer only read production DOM state and recorded trusted events delivered by native XCTest. Escape, Return and Delete through the tested XCTest typeKey API did not produce DOM key events; no physical-keyboard or VoiceOver claim follows from these records. Native accessibility hasFocus can apply to multiple elements, so delayed DOM activeElement and subsequent real input delivery are recorded separately.

All project content was synthetic. Raw fixture backups, simulator container paths and raw private build outputs remain outside the repository. Curated logs retain system/runtime warnings, with repository and temporary harness paths replaced by placeholders.
