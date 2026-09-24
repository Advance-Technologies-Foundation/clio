# ENG-91844 — one column of an element's record as a value source: plan

[ENG-91844](https://creatio.atlassian.net/browse/ENG-91844) "Implement full parameter mapping (sources)".
Scope is the re-scope in Jira comment 518187, core items 0–7. Items 8–9 (SysVariable / SysSettings typed
sources; a column source on connections, approval, performer, access rights) are out.

## Problem

A column of a record another element returned — `ReadContact.ResultEntity.Owner` — could not be named as a
source. There was no field for it, and a dotted `sourceElementParameter` already means a collection-item path
on crt-process-builder main. Guidance turned "describe reports no column UIds" into "it cannot be authored",
so an agent asked for "a call task for the contact's owner unless Do not call" built two filtered signal starts
with a role performer instead of one gateway with `OwnerId <- Contact.Owner`.

## Decisions

| # | Decision | Why |
|---|---|---|
| D1 | A separate field, **`sourceColumn`**, beside `sourceElement` + `sourceElementParameter` on every FLAT source descriptor (mappings / `addMapping`, changeData / addData / openEditPage values, openEditPage `recordId`); **`column`** inside a filter's nested `elementParameter`. | The owner chose `sourceColumn` because `ChangeDataValueDescriptor.column` is already the TARGET column; one name everywhere reads as one triple. The nested filter object has no such clash. The dotted syntax is not reused: it is the collection-item path. |
| D2 | Stored as Source = `Script` with the prefixed three-segment token `[#[IsOwnerSchema:false].[IsSchema:false].[Element:{e}].[Parameter:{p}].[EntityColumn:{c}]#]`. | What the designer writes for an element-column pick (`ProcessParameterSelectionPage.getPreparedFormulaResult`) and what the shipped corpus holds: 595 Script values, 0 Mapping. The runtime turns a single-token Script into a mapping binding with the column as a sub-parameter. |
| D3 | Only an **Entity**-typed root parameter can be drilled. Collections, collection items and Lookup parameters are refused. | The element that owns a record writes its column sub-values (`ProcessInstanceParametersDataWriter`); nothing writes them for a Lookup, so its columns would stay null. |
| D4 | Type-checked by the column's own type and lookup object through `ParameterTypeCompatibility` (new column-source overload). | `Contact.Account` into a Contact `OwnerId` would save green and assign nobody. |
| D5 | A column outside a non-empty `readData.columns` list is refused, and so is a later `readData.columns` that drops a column something reads; the primary column counts as always loaded; a count / aggregation read is refused as a source. | `ReadDataUserTask.AddESQColumns` fetches only the listed columns (plus the primary one), and `HandleResult` never fills `ResultEntity` in function mode; either would read null with no error. The UId form in a raw expression is stored as written and is the one path left unchecked. |
| D6 | Build-path condition names and Formula bodies gain `[#Element.Parameter.Column#]`; modify-path conditions keep the UId form. | `ProcessExpressionNames` is the one owner of the name dialect; the modify path has no expansion by design. |
| D7 | describe reports `sourceElement` / `sourceElementParameter` / `sourceColumn` for such a value, only when the names re-encode to the identical stored value. | A described trio must feed straight back into `addMapping` without rebinding. |
| D8 | The connection reader no longer decodes a column-drilled value as the whole record. | Naming only element + parameter would re-apply as a different binding. |
| D9 | `[RequiresPackage]` floor for create / modify / modify-as-new-version raised to the release archive. | An older server's serializer discards `sourceColumn` and `elementParameter.column` and answers success. |

## Delivery

Three draft PRs, one branch name `feature/ENG-91844-entity-column-mapping`:
crt-process-builder (server), clio (MCP surface, rebundle, floors, E2E), clio-knowledge (guidance, library
version bump). Review gates per `AGENTS.md`.
