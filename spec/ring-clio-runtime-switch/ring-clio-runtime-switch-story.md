# ClioRing clio-runtime switch story

Status: done
Issue: #903

As a ClioRing user, I want the released clio to be the normal runtime and any development runtime to be
obvious, so I do not accidentally manage Creatio with an unintended local build.

## Definition of done

- [x] UI design is rendered and approved by the requester.
- [x] Release mode resolves to a verified `clio mcp-server` dotnet-tool shim from standard dotnet-owned directories.
- [x] Existing development settings migrate without losing their target.
- [x] Main-surface warning and Release/Development selector are implemented.
- [x] IPC workflows, environment discovery, and ordinary radial actions share the selected runtime.
- [x] Settings, schema, and README use consistent terminology.
- [x] Focused Ring tests pass.
- [x] Comprehensive agentic review has no unresolved findings.
- [x] Windows x64 NativeAOT publish passes without trim/AOT warnings.
