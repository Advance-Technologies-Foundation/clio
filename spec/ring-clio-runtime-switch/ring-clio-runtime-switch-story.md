# ClioRing clio-runtime switch story

Status: in-progress
Issue: #903

As a ClioRing user, I want the released clio to be the normal runtime and any development runtime to be
obvious, so I do not accidentally manage Creatio with an unintended local build.

## Definition of done

- [x] UI design is rendered and approved by the requester.
- [x] Release mode resolves to `clio mcp-server` from `PATH`.
- [x] Existing development settings migrate without losing their target.
- [x] Main-surface warning and Release/Development selector are implemented.
- [x] Settings, schema, and README use consistent terminology.
- [x] Focused Ring tests pass.
- [ ] Comprehensive agentic review has no unresolved Blocker/High findings.
- [x] Windows x64 NativeAOT publish passes without trim/AOT warnings.
