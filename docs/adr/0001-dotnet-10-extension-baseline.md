# Use .NET 10 across the host and extensions

Novalist 3.6.0 and SDK 15 move all C# projects in `novalist-official`, `novalist-aiassistant`, and `novalist-extension` to .NET 10; the iOS app already uses .NET 10. We accept requiring Novalist 3.6.0 for new official extension releases rather than retaining .NET 8 targets or maintaining multiple targets, so the host and extension projects share one framework baseline.

Publish the host and SDK first, then release extensions against the `v3.6.0` host tag with `minHostVersion` set to `3.6.0`. Older hosts retain access to earlier compatible extension releases. SDK publication must treat a target-framework change as a breaking change even when the public API is unchanged.
