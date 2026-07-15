# Application-pool profile cleanup ADR

Status: accepted for issue #881

## Decision

Add a DI-backed application-pool profile cleaner in `clio/Common`. The cleaner has a Windows-native adapter that:

1. translates `IIS APPPOOL\<actual-app-pool-name>` to a SID;
2. rejects any SID outside `S-1-5-82-*`;
3. reads the corresponding registered `ProfileImagePath` from Windows ProfileList;
4. calls `DeleteProfileW` with that SID and path;
5. retries native failures three times with short delays and returns a data-only result instead of throwing.

`CreatioUninstaller` captures the app-pool name and validated SID/ProfileList registration before `delete-iis`, runs deletion after IIS/files cleanup and before `unregister`, logs one final warning, emits a warning stage, and completes with the existing `success` terminal. Missing/non-Windows profiles produce `skipped/not-applicable`.

Extend `IStageEventEmitter` with warning-stage completion and the explicit `success-with-warnings` terminal required by the issue contract. ClioRing maps that outcome to its successful warning-aware terminal UI.

## Safety

- No profile path is derived from CLI input.
- SID namespace and ProfileList registration are mandatory authority checks.
- Native error text is reduced to safe Win32 detail; stack traces never enter user output.
- Cleanup failure cannot enter the normal failed-stage cascade.

## Compatibility

The change is additive at the typed-event level. ClioRing is updated in the same PR, its mirrored fixture remains byte-identical to the provider fixture, unknown-field tolerance and ordered replay remain covered, and Ring tests plus Windows x64 NativeAOT publish are mandatory.
