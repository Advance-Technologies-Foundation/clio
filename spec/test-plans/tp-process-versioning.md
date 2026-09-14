# Test Plan: Business process versioning — read, create, set actual

**Feature**: process-versioning
**Jira**: ENG-94374
**Stories**: `spec/stories/story-process-versioning-1.md` … `-18.md`
**Author**: QA Planner Agent
**Status**: Approved — revised after adversarial review 2026-09-02; carried in clio#1410 and crt-process-builder#47, both approved by d-krestov on 2026-09-09
**Created**: 2026-09-02

---

## Scope

### In scope

- The version members of the describe response, their **absence** when not established, the truncation flag, the read warning and the provenance member.
- Version-family projection: ordering, root marking, active marking, the 50-member cap.
- Active-version resolution on caption-addressed paths (`describe --process-caption`, `generate-process-model`) including the empty-set fallback.
- Tool metadata: names, safety flags, `[RequiresPackage]` placement per tool shape, classification rows, route resolution.
- `ModifyProcessAsNewVersion` / `SetActiveProcessVersion` handler behaviour: guard, clone fidelity, shared-applier delegation, atomic abort, allocation, naming, collision, rollback, read-back verdict.
- The bundled-package pin set and the version floor after the rebundle.
- Guidance content for `process-versions`, and clio's curated-name pin.

### Out of scope

- Deleting a version — no product concept, and the platform's delete cancels every `SysProcessLog` row of the schema. Nothing in this feature removes a version: the edits and the new version are saved together, so a rejected edit leaves nothing to remove.
- Migrating running instances between versions.
- `IsMaxVersion` correctness — the field is not surfaced (ADR choice 7).
- `get-process-signature` version-awareness — an explicit PRD non-goal with a follow-up ticket.
- Proving the reported active version is what the runtime starts. The read half reports the process library's verdict with its provenance stated; the authoritative answer is a follow-up.
- PostgreSQL and Oracle stands. The measurements behind this plan were taken on MSSQL; the view variants order NULLs differently.

---

## Traceability

| Story | ACs | Covered by |
|-------|-----|-----------|
| 1 reader | AC-01..06, AC-ERR | TC-U-01..08 |
| 2 describe members | AC-01..06, AC-ERR | TC-U-09..14 (AC-06 by TC-U-11b; TC-U-09 also asserts `versionRootSchemaUId` and `activeVersionSource`) |
| 3 caption resolution | AC-01..04, AC-ERR | TC-U-15..18 |
| 4 MCP surface | AC-01..04, AC-ERR | TC-U-19..21 |
| 5 e2e describe | AC-01..03, AC-ERR | TC-E-01, TC-E-02 |
| 6 guidance article | AC-01..04, AC-ERR | TC-U-22 |
| 7 curated-name pin | AC-01..03, AC-ERR | TC-U-23 |
| 8 spike | evidence only | TC-I-01 (recorded findings, no assertions shipped) |
| 9 contracts/guard/transport | AC-01..04, AC-ERR | TC-U-24, TC-U-25 |
| 10 clone fidelity | AC-01..05, AC-ERR | TC-U-26, TC-I-02 |
| 11 shared applier + apply to the clone | AC-01..05, AC-ERR | TC-U-30, TC-U-30a, TC-U-30b |
| 12 allocate + name + save + collision | AC-01..05, AC-ERR | TC-U-27..29, TC-U-30c, TC-I-03 |
| 13 set-active | AC-01..05, AC-ERR | TC-U-31..34, TC-I-04 |
| 14 package version stamp | AC-01..04, AC-ERR | TC-U-35 (stamp half), TC-I-05 |
| 15 rebundle, pins and floor | AC-01..04, AC-ERR | TC-U-35..37, TC-U-30d, TC-I-05 |
| 16 modify-as-new-version tool | AC-01..05, AC-ERR | TC-U-38..40, TC-U-30d, TC-E-03 |
| 17 set-active tool | AC-01..04, AC-ERR | TC-U-41..43, TC-E-04 |
| 18 write-half guidance | AC-01..04, AC-ERR | TC-U-44 |

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| A version-read failure turns a working describe into an error | Med | High | TC-U-11: the graph still returns, no version key is serialized, and the warning names the failure |
| `versions: []` read as "checked, no versions" | Med | Med | TC-U-12 asserts the member is **absent**, not an empty array |
| Absence of a member indistinguishable from an unversioned process | High | High | TC-U-10 vs TC-U-11: the warning is present in one case and absent in the other |
| A substring scan for absent keys passes vacuously because `[JsonExtensionData]` re-emits a server-sent `version` | Med | High | TC-U-13 asserts **parsed root-object keys**, and TC-U-14 feeds a server payload that already contains `version` |
| New members added to the private wire subclass instead of the public result | Low | High | TC-U-13 serializes by the static type |
| Non-nullable model silently turns NULL into `0`/`false` | High | High | TC-U-07; the model change is story 1's first edit |
| An unprojected family read drags `MetaData byte[]` per member | High | Med | TC-U-08 asserts the projection; the latency figure only holds with it |
| Caption filter hides a genuine ambiguity, or empties the candidate set | Med | Med | TC-U-17 keeps the conflict error for two distinct processes; TC-U-18 covers the empty-set fallback |
| Tool names or flags drift from the contract | Med | Med | TC-U-19/38/41 reflect over the tools. **Note**: kebab-case is a house rule from `project-context.md`, not an analyzer guarantee — CLIO001 is the DI-construction analyzer (`Clio.Analyzers/DependencyInjectionManualConstructionAnalyzer.cs`), and the docs' claim that CLIO001 polices flag casing does not match the implementation. The reflection test is the only real guard |
| `IsActiveVersion` explicit `false` forgotten on create — the property defaults to **true** and is omitted from serialization when true | Med | High | TC-U-26 asserts the assignment, not the payload |
| Version numbered from the UId instead of `SysSchema.Id` — compiles, runs, silently returns 0 | Med | High | TC-U-27 asserts the allocator's arguments; TC-I-03 reads `SysSchemaProperty` |
| Two concurrent creates take the same number | Med | Med | TC-U-29 covers the post-save re-read verdict |
| The two modify paths drift apart, so a new operation works in place but not into a new version | Med | High | TC-U-30 pins that both delegate to the same `IProcessOperationExecutor` |
| **Editing a clone corrupts the live source in memory while its row stays identical** | High | Critical | TC-U-30a. Cloned elements keep a back-pointer to the source (`ProcessSchemaBaseElement.cs:86`), a flow's `SourceRefUId` setter writes into the source's `Outgoings` (`ProcessSchemaSequenceFlow.cs:143-152`), and the source instance is app-cached (`Manager.cs:320-329`). ADR choice 17 removes the mechanism; TC-U-30a is what proves it |
| The clone's `Group` rebinds the source's localizable string | Med | High | TC-U-30a's second assertion; `ProcessSchema.cs:143` + `LocalizableString.cs:270-272` alias it, and `InitializeLocalizableValues` (`:1412`) then repoints it |
| A rejected edit still saves a schema | Med | High | TC-U-30's second half asserts no schema is saved |
| Activation reported successful while two members stay flagged active | Med | High | TC-U-32 forces a read-back mismatch; TC-U-33 asserts the swallowed-failure count surfaces |
| A `[McpServerToolType]` without a classification row | High | Low | the existing suite turns this red on the first build |
| A version floor above the bundled archive | High | High | TC-U-37 is the existing declared-requirement guard; story 15 lands the archive first and each tool story then declares a floor at or below it |
| Pins moved on one side only | Med | High | TC-U-35/36 assert the counts as **deltas** from the baseline at merge time — two other stories rebundle the same package first |
| MCP E2E cannot fail a merge | High | Med | every load-bearing assertion mirrored at unit level; manual gate in the PR checklist |
| Integration tests hit a shared stand and down the app pool | Med | High | TC-I-* are `[Explicit]`, self-ignoring without configuration, `[NonParallelizable]`, and run schema writes sequentially |

---

## Unit Tests (`clio.tests/`, and `crt-process-builder/tests/` where marked)

### TC-U-01: version facts for an unversioned process

```csharp
[Test]
[Category("Unit")]
[Description("A schema with no versions reports version 0, active true, and a single root family member.")]
public void Read_ShouldReportRootOnlyFamily_WhenSchemaHasNoVersions() {
    // Arrange
    DataProviderMock provider = new();
    provider.MockItems(nameof(VwProcessLib)).Returns(OneRootRow(RootUId));
    ProcessVersionLibReader sut = new(provider);

    // Act
    ProcessVersionFacts facts = sut.Read(RootUId.ToString());

    // Assert
    facts.Version.Should().Be(0,
        because: "an unversioned schema is version 0 in the process library");
    facts.IsActiveVersion.Should().BeTrue(
        because: "a one-member family is its own active version");
    facts.Versions.Should().ContainSingle(v => v.IsRoot,
        because: "the only member is the family root");
    facts.Warning.Should().BeNull(
        because: "nothing failed — absence of a warning is how a caller tells this from an unestablished read");
}
```

`MockItems` is an **instance** API on `DataProviderMock` — see `clio.tests/Command/ApplyEnvironmentManifestCommandTests.cs:71-75` for the shape.

### TC-U-02: active version named for a two-member family
Root plus a member flagged active. Assert `IsActiveVersion` false, `ActiveVersionName` and `ActiveVersionSchemaUId` equal to the flagged member's.

### TC-U-03: family ordering, root and active marking
Three rows fed out of order. Assert ascending by version, exactly one `IsRoot`, exactly one active.

### TC-U-04: read failure returns facts with a warning, not an exception
Provider throws the specific DataService/ATF failure. Assert no throw, no version values, warning non-empty.

### TC-U-05: absent row
No row for the UId. Assert the same shape as TC-U-04 with a warning that names the absence.

### TC-U-06: non-Guid identity
`Read("not-a-guid")`. Assert facts with a warning, no throw.

### TC-U-07: NULL from the view is NOT ESTABLISHED
Row whose `Version` and `IsActiveVersion` are null. Assert both facts null — not `0` / `false`. If ATF cannot materialise SQL NULL through the mock, assert at the mapping layer and record the limitation in the story.

### TC-U-08: family read is projected and capped
80 members. Assert 50 returned, `FamilyTruncated` true, and that the executed query projects columns rather than the whole model (the model declares `MetaData byte[]`).

### TC-U-09: describe reports the members for an unversioned process
Through `ServerProcessDescriber` with a reader returning root-only facts. Assert `version` 0, `isActiveVersion` true, one root entry, no warning member.

### TC-U-10: describe names the active version for a versioned root
Assert `isActiveVersion` false and `activeVersionName` set.

### TC-U-11: a failed version read never fails describe
Reader returns facts with a warning. Assert the result is successful, the graph is intact, `versionReadWarning` is present and no version values are serialized.

### TC-U-11b: truncation is reported through describe
A reader reporting `FamilyTruncated` true. Assert `versionsTruncatedAt` is 50 in the serialized output. (Covers story 2 AC-06.)

### TC-U-12: `versions` is absent rather than empty
Assert the serialized output has no `versions` key when the family is not established.

### TC-U-13: parsed-key assertions over the serialized output
Serialize a result carrying every member; assert the parsed root object's key set. Guards both the private-subclass mistake and substring flakiness.

### TC-U-14: a server-sent `version` key does not defeat the absence assertion
Feed a wire payload already containing a root-level `version` (it lands in `[JsonExtensionData]`, `IProcessDescriber.cs:159-165`). Assert the describe output is unambiguous about provenance and that TC-U-12's assertion still discriminates.

### TC-U-15: caption resolves to the active version within one family
Two rows, same caption, same root, one active. Assert the active row is returned, no error.

### TC-U-16: describe's caption path uses the resolver
Assert the describe caption resolution and the generator share `ProcessLibResolver` (one policy, two call sites).

### TC-U-17: genuine ambiguity is preserved
Two rows, same caption, different roots. Assert the conflict error naming both codes.

### TC-U-18: empty candidate set falls back to the ambiguity error
Two same-caption rows whose active flags are null. Assert the ambiguity error, not an arbitrary pick.

### TC-U-19: describe tool metadata and description tokens
Reflect the four flags (unchanged) and assert the description contains the version paragraph, the by-name caveat and the provenance sentence. Assert `[FeatureToggle]` is still absent (`ProcessDesignerGoLiveTests.cs:62` pins it).

### TC-U-20: `run-process` description states it starts the active version
Assert the sentence is present.

### TC-U-21: capability map row
Assert `docs/McpCapabilityMap.md`'s describe row lists the new members. A content assertion, since that file has no other guard.

### TC-U-22: guidance article content *(clio-knowledge)*
Assert `process-versions` contains the flat-family, single-actual, pinned-instances, rollback-affects-new-runs and no-delete statements, on contiguous phrases (line wraps break longer assertions).

### TC-U-23: curated-name pin and drift
Assert the fixture's generation formula, the presence of `process-versions`, and that no ungated tool description names an unknown or gated article.

### TC-U-24 *(package)*: contract reflection
Assert the new operation exists with `BodyStyle = WebMessageBodyStyle.Wrapped` and that the expected operation count is the baseline plus one.

### TC-U-25 *(package)*: guard runs before any repository call
Guard throws. Assert no repository method was invoked.

### TC-U-26 *(package)*: clone fidelity
Assert the fresh schema UId, `CreatedInOwnerSchemaUId` unchanged, caption restored on **both** the manager item and the instance, `ParentSchemaUId` equal to the root, `IsActiveVersion` explicitly false and `IsDelivered` false — assert the assignments, since a default `true` is omitted from the payload.

### TC-U-27 *(package)*: allocation arguments
Substitute the manager and assert `GetMaxProcessVersionInPackage` was called with the root item's **`Id`**, not its `UId`.

### TC-U-28 *(package)*: name collision refusal
`ProcessExists` returns true. Assert refusal before the save, message names the collision.

### TC-U-29 *(package)*: post-save re-read verdict
Family re-read shows the number taken by another writer. Assert `success:false` naming the observed number.

### TC-U-30 *(package)*: one applier, two entry points
Assert both `ModifyProcess` and `ModifyProcessAsNewVersion` delegate every descriptor to the same `IProcessOperationExecutor` instance and run the same post-loop steps (layout, localizable values, pre-save validation). Then assert that a rejected operation in the new-version path saves **no** schema — the atomicity that removes the need for any compensating delete.

### TC-U-30a *(package)*: the source instance is untouched in memory
The decisive test for choice 17, and the one no row comparison can replace. Apply an edit list containing `addFlow` and `removeElement` to a clone of a source whose instance is in the manager cache, then assert on the SOURCE instance: every element's `Outgoings`/`Incomings` count is what it was, and `source.Group` still carries the source's own resource-manager name. Without choice 17 both assertions fail while the source's database row stays byte-for-byte identical.

### TC-U-30b *(package)*: empty edit list yields an exact snapshot
Covers PRD AC-05d and story 11's own note: an absent or empty `operations` list saves a new inactive version that matches the source field for field.

### TC-U-30c *(package)*: a false save is handled as a failure
`SaveSchema` returns `false` without throwing when source generation fails (`SchemaManager.cs:1666-1685`). Assert `success:false` with a message, the draft rolled back, and no family member added — the in-place path's own handling at `ProcessModifyHandler.cs:90-97` is the reference.

### TC-U-30d: a stale package is refused before any write
Covers PRD AC-10. Extend the existing stale-package detector cases (`clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs:144`) to both new options classes, and assert the refusal names the required version and the `install-process-builder` hint.

### TC-U-31 *(package)*: activation happy path
Assert the response carries the read-back active version.

### TC-U-32 *(package)*: read-back mismatch
Manager reports a different member active. Assert `success:false` naming it.

### TC-U-33 *(package)*: swallowed sibling deactivation surfaces
Simulate a sibling deactivation failure. Assert `deactivationFailureCount` non-zero and the result not reported as plain success.

### TC-U-34 *(package)*: already-active is a no-op success
Assert success without a state change.

### TC-U-35: bundled-archive pins agree
Assert archive version, SHA-256 and both descriptor stamps match the archive.

### TC-U-36: operation count and gate call sites move as deltas
Assert both counts equal the baseline at merge time plus two — not hard-coded 7 and 5, because two other stories rebundle this package first.

### TC-U-37: declared requirements do not exceed the archive
The existing `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` guard, exercised with the new versioned floors.

### TC-U-38 / TC-U-41: tool metadata for the two new tools
Names equal the `internal const` values; flags equal the ADR table; `[RequiresPackage]` present with a **version** and placed per the tool's shape (options type for a `BaseTool<T>` derivative, args record for a standalone tool calling `EnsureRequirements`), with the `[TestCase]` added to the matching list in `ProcessDesignerRequiresPackageAttributeTests`.

### TC-U-39 / TC-U-42: argument validation
Neither identity supplied, and both supplied. Assert the MCP result is `success:false` naming the violation — the envelope, not a process exit code.

### TC-U-40 / TC-U-43: route resolution
Assert each tool resolves its `ServiceUrlBuilder.KnownRoute` to `"/rest/ProcessDesignService/<Operation>"` — with the leading slash, matching the five sibling `ProcessDesignService` entries at `ServiceUrlBuilder.cs:315-322`; without the route clio cannot reach the package at all.

### TC-U-44: write-half guidance content *(clio-knowledge)*
Assert the two-step order (`modify-business-process-as-new-version` then `set-active-business-process-version`), the ask-once protocol, the session-policy-is-behaviour statement, and that a rejected edit saves nothing so no version needs cleaning up and no delete exists.

---

## Integration Tests

All four are `[Category("Integration")]`, `[Explicit]`, `[NonParallelizable]`, and **self-ignore with a message naming the missing setting** when no sandbox is configured. They write schemas, so they run **sequentially** — a parallel burst trips IIS rapid-fail and downs the .NET Framework app pool.

### TC-I-01: spike measurements *(story 8, evidence only)*
- **Setup**: dedicated sandbox with the package installed.
- **Steps**: the seven measurements from story 8.
- **Expected**: findings recorded in the ADR Notes; nothing asserted in CI.

### TC-I-02: clone diffed against a designer-created version
- **Setup**: one source process; one version created by the package, one created by hand in the designer.
- **Expected**: a field-by-field diff with every difference explained; attached to the PR. A unit test cannot establish this.

### TC-I-03: two consecutive saves number 1 then 2, and a rejected edit changes nothing
- **Steps**: save a new version, read `SysSchemaProperty`; save again, read again; then attempt a save whose edit list contains a rejected operation.
- **Expected**: `Version` rows `'1'` then `'2'`, `IsActiveVersion` `'False'` on both, the root's own rows unchanged, and the family still at three members after the rejected save — nothing was persisted.
- **Pin the package** explicitly (`package-name`), because the allocator is package-scoped and PRD OQ-01 is still open.

### TC-I-04: activation verified by read-back
- **Steps**: set the older member active; read the active version through the manager and through describe.
- **Expected**: both agree with the request; exactly one member active.
- **Teardown**: restore the previously active version.

### TC-I-05: install after the rebundle
- **Steps**: rebuild clio, `install-process-builder --force -e <sandbox>`, then call the package service.
- **Expected**: install completes; the service answers; the tools' version floor is satisfied.

---

## E2E Tests (`clio.mcp.e2e/`)

**CI status for all of them**: MCP E2E runs in CI as an **advisory, non-blocking** check — it cannot fail a merge, it is path-filtered, and its ~45-minute build can be superseded by a later push. The process-designer fixtures additionally need `CrtProcessBuilder` installed on the target stand. Locally, against a stand carrying the package, the describe fixture runs green in about a minute. **Manual gate**: PR checklist entry for each.

### TC-E-01: describe reports the version members
- **Tool**: `describe-business-process`
- **Input**: `{"process-name": "InvoiceVisaProcess", "environment-name": "<sandbox>"}`
- **Expected**: `isActiveVersion: false`, `activeVersionName: "InvoiceVisaProcessInvoice1"`, `versions` with two members, `activeVersionSource: "process-library-view"`

### TC-E-02: describe by the active version's UId
- **Expected**: `isActiveVersion: true`, both members listed ascending.

### TC-E-03: save edits as a new version
- **Tool**: `modify-business-process-as-new-version`; behind `McpE2E:AllowDestructiveMcpTests=true`, **dedicated sandbox only**.
- **Expected**: `versionName`, `version: 1`, `isActiveVersion: false`, `versionRootSchemaUId`.

### TC-E-04: set a version active and read it back
- **Tool**: `set-active-business-process-version`; same opt-in and sandbox rule.
- **Expected**: `activeVersionName` equal to the requested version, `success: true`; a following describe agrees.

---

## Regression Guard

| Test file | Test name | Why at risk |
|-----------|-----------|------------|
| `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` | all tests | the constructor gains a dependency — a deliberate compile break |
| `clio.tests/Command/DescribeProcessCommandTests.cs` | serialization assertions | the response gains members; `WriteIndented` makes assertions format-sensitive |
| `clio.tests/Command/ProcessLibResolverTests.cs` | existing ambiguity cases | the resolver gains an active-version filter (note the path — `Command/`, not `Command/ProcessModel/`) |
| `clio.tests/Command/McpServer/DescribeProcessToolTests.cs` | flag reflection pins, description tokens | the `[Description]` grows |
| `clio.tests/Command/McpServer/ProcessDesignerGoLiveTests.cs` | feature-toggle absence pin | restoring `[FeatureToggle]` on any process tool turns it red |
| `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` | shipped-template and ungated-guidance checks | descriptions and guidance names change together |
| `clio.tests/Command/McpServer/ProcessDesignerEmittedSchemaTests.cs` | emitted input schema pins | two new tools emit new schemas |
| `clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs` | presence and versioned requirement lists | two new options classes; the class `[Description]` counts them |
| `clio.tests/Command/McpServer/DurableInvocationGateCompletenessTests.cs` | reviewed-silently-executable list | mutating tools must not be added to it |
| `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | archive version / SHA / stamps / counts / declared requirements | the rebundle moves all of them |
| `crt-process-builder/tests/.../ProcessDesignServiceWireContractTests.cs` | operation count and per-operation `BodyStyle` | two operations added |

Smart-regression note: the read-half stories touch Command, McpServer and ProcessModel — exactly three modules, so a targeted filter is legal there. The write-half stories edit `BindingsModule.cs` (two command registrations), which is an explicit **full-suite** trigger and forces the full three-lens review. Name the filter used in every PR body.

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit (clio) | 32 | ~28 | the describer suite recompiles; command, tool, drift and pin suites extended |
| Unit (package) | 15 | 2 | wire-contract and gate-call-site pins |
| Integration | 5 | 0 | all `[Explicit]`, sequential, dedicated sandbox |
| E2E | 4 | 1 | advisory in CI; manual gate per fixture |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` in clio — never `[Category("UnitTests")]`; in the package repo the fixture-level `[TestFixture(Category = "UnitTests")]` is the correct convention, and the two must not be "harmonised"
- [ ] All TC-I-* implemented with `[Category("Integration")]`, `[Explicit]`, `[NonParallelizable]` and a self-ignore naming the missing setting
- [ ] Every assertion carries a because-clause
- [ ] Every story AC maps to at least one test case, and every test case maps back to an AC (Traceability table kept current)
- [ ] Regression guard green, and the filter used named in the PR body
- [ ] MCP E2E documented as an advisory, path-filtered, supersedable check with a manual gate; destructive fixtures pointed at a dedicated sandbox
- [ ] Test naming follows `MethodName_ShouldBehavior_WhenCondition`
- [ ] Each new response member asserted on the **parsed serialized output**, not only on the DTO
- [ ] Pin assertions expressed as deltas from the baseline at merge time
- [ ] PR includes the test files in the changed-files list
