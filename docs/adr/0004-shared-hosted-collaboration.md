# Share hosted collaboration across desktop editions

The storage and owner-offline requirements below are superseded by
[ADR 0007](0007-relay-only-collaboration.md). Both desktop editions use the same
packet relay with an online host; optional Steam sessions remain available.

Both the open-source and Steam editions will connect to the same hosted collaboration service so their users can work together. Hosted projects retain the owner-independent availability required by [ADR 0003](0003-hosted-project-availability.md). The Steam edition additionally offers Steamworks sessions that do not require a hosted project server. Their hosting writer must remain online for live collaboration; participants retain their copies under [ADR 0005](0005-retain-collaborator-work.md).

Use the existing community [novalist-sync-server](https://github.com/QF0xB/novalist-sync-server) and its current protocol as the integration baseline. Implement client responsibilities against that service first, and establish any missing server behavior with concrete integration evidence. Where server changes are necessary, prepare focused upstream pull requests with the missing behavior and relevant validation. Server changes are conditional on demonstrated need.

Mixed Steam/non-Steam groups use the shared hosted route. The collaboration document model and project semantics should work across both connection options, while each adapter follows its service's protocol. This preserves one collaboration feature across editions and avoids making the hosted service dependent on Steam identities. The existing server's gaps are recorded in the [feasibility assessment](../research/realtime-collaboration-feasibility.md); selecting it does not establish that every requested capability is already implemented.

A project uses one collaboration connection mode at a time. Changing connections means fully closing the current session and starting a new one through the ordinary start/join flows. Novalist will have no dedicated mode-switch feature, automatic handover, or cross-session reconciliation. Normal session closure must preserve local work; pending updates from an old session must not be replayed into a different session. Simultaneous use of both connections is outside the accepted design.
