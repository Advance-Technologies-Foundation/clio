# Key validator: unknown and wrongly-cased keys with path and hint

**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) (work package C — the rescope is not yet agreed with the reporter)
**Spec**: [spec-descriptor-strict-keys.md](../prd/spec-descriptor-strict-keys.md) ·
**ADR**: [adr-descriptor-strict-keys.md](../adr/adr-descriptor-strict-keys.md) ·
**Test plan**: [tp-descriptor-strict-keys.md](../test-plans/tp-descriptor-strict-keys.md)
Status: in-progress

As an agent building a process I want every key the server would drop to be reported with its JSON path and
the nearest valid key, so a typo is fixed instead of silently lost.

Acceptance:
- [ ] `IProcessDescriptorKeyValidator` walks a create descriptor (root `BuildProcessRequest`) or an
      operations array (items `ProcessOperationDescriptor`) against the schema, case-sensitively.
- [ ] Hint: exact case-insensitive match ("keys are case-sensitive"), else closest by edit distance, else the
      valid keys at that level. TC-U-03..TC-U-08, TC-U-12.
