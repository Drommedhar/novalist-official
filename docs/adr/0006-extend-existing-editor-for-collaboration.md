# Extend the existing editor for collaboration

Implement collaboration by adding the necessary document binding, remote-change application, presence, and recovery support to Novalist's existing editor. Preserve its current editing behavior and features wherever possible. An editor replacement or migration is not part of the selected approach.

The editor currently reports whole HTML documents and uses browser editing behavior, so a custom collaboration integration requires careful handling of formatting, selection, composition input, annotations, and per-writer undo. Validate those behaviors in a focused experiment before expanding the feature. If preserving the existing editor proves infeasible, report the specific limitation and revisit the design rather than silently substituting another editor.

The accepted initial disconnected-editing scope is already-synchronized prose. Structural changes, asset changes, membership changes, and project snapshot restoration require a connection. This bounds the initial recovery work while connected collaboration continues to cover the entire project. See the [feasibility assessment](../research/realtime-collaboration-feasibility.md).
