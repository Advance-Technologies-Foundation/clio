# ClioRing clio-tool update story

Status: review
Issue: #905

As a ClioRing user, I want to see and install clio updates while understanding which MCP hosts are using clio,
so I can choose whether to interrupt those processes or cancel safely.

## Definition of done

- [x] Existing clio update, Ring runtime, tray, and process behavior is traced.
- [x] Live Claude-owned clio MCP process evidence is recorded without terminating it.
- [x] Main update-available and lock-holder confirmation UI designs are rendered and approved.
- [x] Version check is bounded, stable-only, and notification-deduplicated.
- [x] Update runs only after explicit user action and verifies the installed version.
- [x] Ring's owned clio child stops gracefully before update.
- [x] Lock holders and parent applications are displayed without persisting process details.
- [x] Kill-and-retry revalidates exact path/start identity and never kills parent hosts.
- [x] Docs and settings schema are aligned.
- [x] Focused Ring tests pass.
- [x] Comprehensive agentic review has no unresolved findings.
- [x] Windows x64 NativeAOT publish passes without trim/AOT warnings after review fixes.
