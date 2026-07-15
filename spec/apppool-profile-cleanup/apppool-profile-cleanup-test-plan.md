# Application-pool profile cleanup test plan

## Unit

- Native/profile cleaner: deleted first try; absent; non-Windows; locked/access denied retried exactly three times; resolution/native failure becomes safe warning; SID namespace is enforced.
- Uninstaller: actual app-pool name and registration are captured before pool deletion; done, skipped/not-applicable, and warning paths; warning does not prevent unregister; exactly one warning; success-with-warnings terminal.
- Command/MCP: exit code remains 0, `WarningMessage` is preserved, progress warning and terminal outcome are forwarded, `IsError` remains false.
- Contract: provider and Ring serialize `warning` with the existing `success` terminal; mirrored fixtures are byte-identical; unknown fields, previous-consumer behavior, and ordered replay remain tolerant.
- Ring: yellow warning step, friendly message with expandable detail, completed-with-warnings header, successful refresh behavior.

## E2E and compatibility

- Run the real MCP server against an explicitly opted-in disposable local Creatio environment while holding the app-pool profile locked; assert warning log, exit 0, `IsError=false`, warning stage, and success-with-warnings terminal.
- Run targeted Command/Common/MCP tests, full ClioRing tests, contract/harness coverage, and Windows x64 NativeAOT publish.

The supplied `F:\CreatioBuilds\10.0.0\10.0.0.802_StudioNet8_Softkey_PostgreSQL_ENU.zip` is used only if the destructive sandbox E2E needs a fresh disposable environment.
