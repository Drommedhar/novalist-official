# Use a packet relay without project storage

The user's clarification on 2026-10-22 supersedes ADRs 0003 and 0004 wherever
they require server-persisted projects or owner-offline joining.

The collaboration server forwards packets between connected clients. It must
not persist project files, project snapshots or edit history, including encrypted
copies. Project data remains on participants' computers. Joining a session
requires its host to be online. Temporary transport buffering does not provide
offline availability or become durable storage.

Initial synchronization compares project-relative paths and SHA-256 hashes on
the clients. An existing local copy supplies matching files; only missing or
changed files cross the connection. The desktop shows progress during scanning,
comparison and transfer, with file and byte counts when available.

This replaces the earlier persistent hosted implementation. Transport, initial
sync, desktop controls and integration tests must use the relay contract.
Earlier successful tests of server-persisted archives are not evidence of
compliance with this architecture.

All changes and tests remain local. Publishing the server changes still requires
the user's explicit approval after their own testing.
