# Test plan: page JSON comments (#1641)

- Red/green: ordinary line and block comments inside paired view-config markers fail before the parser change and pass afterwards.
- Preserve structure: removing an opening marker for each required section still fails.
- Preserve validation: malformed content and a non-localized caption still fail with comments present.
- Preserve payload: validated update-page calls retain the exact body in SaveSchema payload, including marker pairs and comments (mocked service boundary).
- Exercise native trailing-comma support with a following comment and quoted comment-like data.
- Run Command and McpServer unit modules.
- Real Windows stdio MCP: baseline and comment fixtures pass; missing-marker fixture fails. Files previously synchronized to Omen via Mutagen with matching hashes.
- Cleanup: verify disposable CR, runtime resources, PVC/PV and database absent, Redis slot empty, local registration and sync session removed.

Limits: no registered Creatio page rendering or live SaveSchema validation; no debugger work in this issue.
