# Identity uninstall validation

TC-U-01: absent/null/all-empty attachment serializes as empty fields and retains legacy behavior.
TC-U-02: registration update and rename preserve attachment; compare-and-update refuses stale target.
TC-U-03: deployment records target before artifacts, including no-app and failed OAuth verification.
TC-U-04: cleanup rejects incomplete/unsafe/overlapping/shared/replaced targets before mutation.
TC-U-05: standalone clears matching CRM references in safe order and checkpoints before IIS removal.
TC-U-06: shared pools survive, missing artifacts converge, failure retains authority, retry is bounded.
TC-U-07: combined validates CRM configuration first, removes identity before CRM, drops DB once.
TC-U-08: CLI and MCP match args, errors and destructive metadata; explicit skip reports stale CRM references.
TC-I-01: settings schema and serialization/atomic update roundtrip.
TC-I-02: real disposable IIS standalone and combined smoke, checking retained and removed targets/files/database.
TC-E-01: real MCP process discovery/invocation and negative cases, plus opt-in disposable cleanup.
TC-R-01: Ring regression suite, manifest replay/harness, Windows x64 NativeAOT publish.
