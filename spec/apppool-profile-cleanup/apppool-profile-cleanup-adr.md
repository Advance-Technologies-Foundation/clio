# Application-pool profile cleanup ADR

Status: accepted for issue #881

## Decision

Add a DI-backed application-pool profile cleaner in `clio/Common`. The cleaner has a Windows-native adapter that:

1. translates `IIS APPPOOL\<actual-app-pool-name>` to a SID;
2. rejects any SID outside `S-1-5-82-*`;
3. reads the corresponding registered `ProfileImagePath` from Windows ProfileList;
4. calls `DeleteProfileW` with that SID and path;
5. retries native failures three times with short delays and returns a data-only result instead of throwing.

`CreatioUninstaller` captures the app-pool name and validated SID/ProfileList registration before `delete-iis`, runs deletion after IIS/files cleanup and before `unregister`, logs one final warning, emits a warning stage, and completes with the `success-with-warnings` terminal. Missing/non-Windows profiles produce `skipped/not-applicable`.

Extend `IStageEventEmitter` with warning-stage completion and the explicit `success-with-warnings` terminal required by the issue contract. ClioRing maps that outcome to its successful warning-aware terminal UI.

## Safety

- No profile path is derived from CLI input.
- SID namespace and ProfileList registration are mandatory authority checks.
- Native error text is reduced to safe Win32 detail; stack traces never enter user output.
- Cleanup failure cannot enter the normal failed-stage cascade.
- IIS application metadata is parsed fail-closed before target deletion; a root site with sibling
  applications or a nested application with descendants is not removed.
- Pool assignments are queried again after target deletion. The pool is removed only when the fresh,
  complete snapshot has no assignments, and profile cleanup runs only after pool absence is verified.
- Stop and delete authorization remains bound to the originally resolved site/application name,
  normalized physical path, and pool; a same-name replacement aborts before any IIS mutation.
- Pool removal omits a separate stop operation and requires both zero assignments and verified absence
  in fresh post-delete snapshots before profile cleanup can proceed.

## Compatibility

The change is additive at the typed-event level. ClioRing is updated in the same PR, its mirrored fixture remains byte-identical to the provider fixture, unknown-field tolerance and ordered replay remain covered, and Ring tests plus Windows x64 NativeAOT publish are mandatory.
