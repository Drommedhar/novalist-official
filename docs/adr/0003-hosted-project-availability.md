# Keep hosted projects available while their owner is offline

Superseded by [ADR 0007](0007-relay-only-collaboration.md) on 2026-10-22.
The server is now a packet relay; joining requires an online host. The original
decision below is retained as history.

Hosted projects must remain available to invited collaborators while the project owner's application is offline. Writers connect to a hosted service so collaboration does not require inbound connections to their desktops or an always-connected owner.

This includes a collaborator's first join. Owners create expiring, single-use
invitation links that require sign-in. The recipient must be able to claim
access, obtain the shared editing state and project files, and start editing
after the owner disconnects. Private joining material stays in the invitation
link and client storage; the server must not receive plaintext project keys.

This requires durable shared project state and assets to be available independently of the owner's machine after publication. A service that only forwards traffic between currently connected desktops cannot satisfy the requirement. The additional storage, recovery, and access-management work supports collaborators working at different times.

The community novalist-sync-server is the selected hosted integration baseline, as recorded in [ADR 0004](0004-shared-hosted-collaboration.md). Its remaining capability gaps and the client implementation are still under evaluation. The accepted initial disconnected-editing scope covers already-synchronized prose, with structural changes, asset changes, and snapshot restores requiring connectivity. Merge and recovery still need validation. Collaborators retain local copies under [ADR 0005](0005-retain-collaborator-work.md). Steam collaboration sessions have a separate lifetime tied to their hosting writer. See the [feasibility assessment](../research/realtime-collaboration-feasibility.md).
