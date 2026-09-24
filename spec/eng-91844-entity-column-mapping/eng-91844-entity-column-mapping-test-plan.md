# ENG-91844 — one column of an element's record as a value source: test plan

## Unit (crt-process-builder, `tests/UnitTests/CrtProcessBuilder.Tests`)

| ID | Case | Where |
|---|---|---|
| TC-U-01 | The stored token is the prefixed three-segment form | `RecordColumnSourceTests.BuildReference_*` |
| TC-U-02 | Collection, shaped collection, collection item, Lookup and unknown parameters are refused by name | `ResolveRecordParameter_ShouldRefuse_*` |
| TC-U-03 | A column resolves by code; a path, an unknown column and an empty name are refused | `ResolveColumn_*` |
| TC-U-04 | A column outside a non-empty `readData.columns` is refused; listed or empty resolves | `ApplyMapping_SourceColumn_ShouldRefuseAColumnTheReadDoesNotLoad` |
| TC-U-05 | Column-source compatibility: Contact.Owner fits a Contact lookup; Account and Text do not | `AreCompatible_ColumnSource_*` |
| TC-U-06 | `OwnerId <- ReadContact.ResultEntity + sourceColumn Owner` stores a Script source with the token | `ApplyMapping_SourceColumn_ShouldStoreTheColumnTokenAsAScriptSource` |
| TC-U-07 | An incompatible column is refused and writes nothing | `ApplyMapping_SourceColumn_ShouldRefuseAnIncompatibleColumn` |
| TC-U-08 | `sourceColumn` without the element pair, or beside another source, is refused | `ApplyMapping_SourceColumn_ShouldRequireTheElementSource` |
| TC-U-09 | describe names the trio, and the trio re-applies to the identical value | `DecodeRecordColumnSource_ShouldNameTheTrio_*` |
| TC-U-10 | describe leaves the short form, a token inside a formula and an unknown column unnamed | `DecodeRecordColumnSource_ShouldLeaveAValueItCannotReapplyUnnamed` |
| TC-U-11 | A Modify data value stores the token and describes back to the trio | `ChangeData_SourceColumn_ShouldStoreTheTokenAndDescribeBackToTheTrio` |
| TC-U-12 | A Modify data value refuses a missing element source and an incompatible column | `ChangeData_SourceColumn_ShouldRefuse*` |
| TC-U-13 | A filter's `elementParameter.column` stores the unmasked path | `Filter_ElementParameterColumn_*` |
| TC-U-14 | `[#Read.ResultEntity.Column#]` expands; `[#Lookup.X.Y#]` passes through | `ConditionNames_ThreeSegments_ShouldExpand*` |
| TC-U-15 | A path, an unknown column, a collection, a lookup and an unknown parameter in a three-segment name are refused | `ConditionNames_ThreeSegments_ShouldRefuseWhatCannotResolve` |
| TC-U-17 | Open edit page `recordId.sourceColumn` stores the token; a text column and a lookup to another object are refused | `OpenEditPageConfigBinderTests.Apply_RecordIdSourceColumn_*` |
| TC-U-18 | The describer decodes every element and process parameter with the loaded schema | `ProcessDescriberTests.Describe_ShouldDecodeRecordColumnSources_*` |
| TC-U-19 | A count / aggregation read is refused as a column source, and describe names none | `RecordColumnSourceTests.SourceColumn_ShouldBeRefused_WhenTheReadRunsInFunctionMode` |
| TC-U-20 | The primary column is accepted outside a column selection | `SourceColumn_ShouldAcceptThePrimaryColumn_OutsideTheSelection` |
| TC-U-21 | `setElement readData.columns` that drops a read column is refused; keeping it or `[]` passes | `ReadDataConfigApplierTests.Apply_ShouldRefuseAColumnSelectionThatDropsAReadColumn` |
| TC-U-22 | A pathological stored value neither fails nor stalls describe | `DecodeRecordColumnSource_ShouldNotFailDescribe_OnAPathologicalValue` |
| TC-U-23 | On a modify without a page, `recordId.sourceColumn` is checked against the stored object | `OpenEditPageConfigBinderTests.Apply_RecordIdSourceColumn_ChecksTheStoredObject_WhenNoPageIsSent` |
| TC-U-24 | The connection reader returns the raw expression for a column-drilled connection | `EntityConnectionReaderTests.Read_ShouldNotDecodeAColumnReference_AsTheWholeRecord` |

Command: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`.

## Unit (clio)

- `ProcessDesignerRequiresPackageAttributeTests` pins the raised floors.
- `ServerProcessDescriberTests.Describe_ShouldReadTheRecordColumnSource_WhenServerReportsIt` and
  `DescribeProcessCommandTests.Execute_ShouldWriteTheRecordColumnSource_OnlyWhenPresent` pin the DTO both ways.
- `BundledProcessBuilderPackageTests` pins the archive and checks every floor sentence against the literal.
- `ToolContractPayloadBudgetTests` keeps the create / modify contracts inside the payload budget.

Command: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel|Module=Common)"`.

## MCP E2E (clio.mcp.e2e, manual, needs a stand with the release archive)

| ID | Case | Where |
|---|---|---|
| TC-I-01 | create builds Read contact → gateway on `[#ReadContact.ResultEntity.DoNotUseCall#]` → Perform task with `OwnerId <- sourceColumn Owner`, plus a Modify data value with `sourceColumn` and a filter with `elementParameter.column`; describe reports the trio and the expanded condition | `RecordColumnSourceToolE2ETests.CreateBusinessProcess_Should_MapAndBranchOnReadRecordColumns` |
| TC-I-02 | modify `addMapping` with `sourceColumn` applies; `Account` is refused naming the column | `ModifyBusinessProcess_Should_AddAColumnMapping_AndRefuseAnIncompatibleOne` |
| TC-I-03 | A `sourceColumn` path is refused at build | `CreateBusinessProcess_Should_RefuseASourceColumnPath` |

## Stand (manual)

| ID | Case | Evidence |
|---|---|---|
| TC-S-01 | Item 0 probe: the three-segment token saves into `OwnerId`, runs, and the created Activity's Owner is the contact's owner | recorded in the PR description |
| TC-S-02 | The motivating scenario end to end: Contact added → Read contact (Id = RecordId) → gateway on DoNotUseCall → Perform task with `OwnerId <- Owner`; both branches run on a matching and a non-matching contact | recorded in the PR description |

Browser (designer) read-back is the owner's check, not the agent's.
