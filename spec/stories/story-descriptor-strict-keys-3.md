# Guard on create, modify and modify-as-new-version with version-skew policy

**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) (work package C — the rescope is not yet agreed with the reporter)
**Spec**: [spec-descriptor-strict-keys.md](../prd/spec-descriptor-strict-keys.md) ·
**ADR**: [adr-descriptor-strict-keys.md](../adr/adr-descriptor-strict-keys.md) ·
**Test plan**: [tp-descriptor-strict-keys.md](../test-plans/tp-descriptor-strict-keys.md)
Status: review

As an agent I want a call carrying an unknown key to be refused before anything is sent, except on an
environment whose CrtProcessBuilder is newer than the bundled one, where it is a warning.

Acceptance:
- [x] `IProcessDescriptorKeyGuard` refuses when the environment is not newer than the bundle, warns when it
      is newer or when a version cannot be read, and reads the installed version only when a key was found.
- [x] Called from `CreateBusinessProcessService`, `ModifyBusinessProcessService` and
      `ModifyProcessAsNewVersionService` right after parsing — before the page-facts check and the POST.
- [x] The per-block read-backs stay. TC-U-09, TC-U-10, TC-U-11, TC-U-13, TC-U-14.
