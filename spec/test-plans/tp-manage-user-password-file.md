# Test Plan: manage-user password file

**Feature**: manage-user-password-file
**Stories**: [story-1](../stories/story-manage-user-password-file-1.md)
**ADR**: [adr-manage-user-password-file.md](../adr/adr-manage-user-password-file.md)
**Status**: Implemented
**Created**: 2026-11-14

| ID | Test | Proves |
|---|---|---|
| TC-U-01 | `ClioSecretFileReaderTests.Read_ShouldReturnTheSecretWithoutTheTrailingLineBreak`, `Read_ShouldDropAByteOrderMark` | The secret is read by name; a trailing line break and a BOM are dropped, spaces kept. |
| TC-U-02 | `ClioSecretFileReaderTests.Read_ShouldRefuseAValueThatIsNotAPlainName`, `Read_ShouldRefuseATooLongName` | A path, a hidden name or a pasted secret is refused and not echoed. |
| TC-U-03 | `ClioSecretFileReaderTests` missing, others-can-read, link, linked folder, empty, too large, multi-line, not UTF-8 | Every refusal, none repeating the content. |
| TC-U-04 | `AdministrationPasswordReferenceTests.Execute_AcceptsPasswordFile` | create and password send the file's content and never print it. |
| TC-U-05 | `AdministrationPasswordReferenceTests.Execute_RejectsBothPasswordReferences`, `Execute_RefusesAPasswordFileItCannotRead` | Ambiguous or unreadable references fail before the service is called. |
| TC-U-06 | `ManageUserToolPasswordFileTests` | The MCP tool refuses before resolving the environment. |
| TC-E-01 | `AdministrationContractToolE2ETests.ManageUser_RefusesAPasswordFileThatIsNotAPlainName`, `ManageUser_RefusesBothPasswordReferences` (`McpE2E.NoEnvironment`) | The real stdio server refuses, without echoing. |
| TC-E-02 | `AdministrationToolE2ETests.Administration_Lifecycle(false, true)` (`McpE2E.Sandbox`) | An account is created from an owner-only file in clio's secrets folder. |
