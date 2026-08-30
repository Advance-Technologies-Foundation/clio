# Creatio-aware three-way merge first lab run

## Scope and result

The first live run on 2026-08-22 proved the resolver engine against a real Git three-way conflict
created by two independent Creatio instances. The merge result was committed, packaged from the
exact merge commit, installed on both instances, compiled, and read back successfully.

The live run invoked the resolver CLI directly from source snapshot
`e65852f9521b2c1d288883428b3dd7ebb6fc73be`. The rights holder subsequently authorized that source
for public modification and MIT redistribution in clio. The preserved stages are now also exercised
through the real clio CLI and MCP process; the packaged-release and live rerun controls remain before
feature closure.

## Retained lab

- Root: `F:\Projects\Issue-Workspaces\issue-1183`
- Integration workspace: `main`, branch `main`
- Pretend-human workspace A: `developer-a`, branch `developer-a`, environment `issue-1183-a`
- Pretend-human workspace B: `developer-b`, branch `developer-b`, environment `issue-1183-b`
- One local bare origin: `origin.git`
- Creatio version on A and B: `10.1.585.0`, .NET `8.0.30`, PostgreSQL
- Package: `UsrMergeProof`, UId `a5b4a193-d124-48d3-96ab-60a976aa9e9e`
- EntitySchema: `UsrMergeProofEntity`, UId `a2aba7b4-2289-4731-942a-7c5dcb4fd684`

No credentials or connection strings are stored in this document or the Git repository.

## Commit graph

| Role | Commit | Assertion |
|---|---|---|
| B0 | `ea85b7d60c5038deb9a027cd8d26e2cccf952bcb` | Common entity with `UsrName` only |
| A1 | `2f9b30613ff2f392f8d77e32304021c5a07fa93c` | Adds only `UsrDeveloperAText` through Creatio A |
| B1 | `f404a2639e2918e2e90014542041bea6da184620` | Adds only `UsrDeveloperBNumber` through Creatio B |
| Merge | `3c8581bd3b09740fa15c044f5f0d3d558360fd9e` | Parents are exactly A1 and B1; contains both columns |

A1 and B1 both have B0 as their direct parent. All three branches were pushed to the retained local
origin.

## Real conflict evidence

Git reported content conflicts in the EntitySchema metadata and its `en-US` resource. The stage
entries were:

| Artifact | Stage 1 | Stage 2 | Stage 3 |
|---|---|---|---|
| `Schemas/UsrMergeProofEntity/metadata.json` | `3184e9a70a234f4b25e6c996a918621df7502025` | `f98f102a84d60b214a7f365878577d985a5d75ad` | `70e3615ef2e354bebb803eb7c1663fd1d766a1de` |
| `Resources/UsrMergeProofEntity.Entity/resource.en-US.xml` | `3d68b1e68cdfa192e4bf8785e102352169ab14b6` | `17560648dfbd0d5c74fde9d73c3b71ffa0b0aacb` | `2e03fe29f5f7d376ac10ca4a9d1a2e239c406331` |

Exact stage files and hashes are retained beneath
`evidence\entity-schema-conflict` outside the clones.

## Resolver outcome

The metadata result was `Resolved`, resolution type `json_3way_local_win`, with no true conflicts
and resolver verification passed. The resource result was `Resolved`, resolution type
`name_union_local_win`, with the A and B captions reported as independent additions and no true
conflicts.

| Result | Git blob | SHA-256 |
|---|---|---|
| EntitySchema metadata | `a5ac0327a001298238365a05ab29f84020140a3e` | `A962268BB72D00EA1EC18A1B356CE8A14BF263EFA0C260E7E26AB331A038BCE2` |
| EntitySchema resource | `5c08c4eb7e81c9f5a8684955f31e912a3aaf5d41` | `6BA0EF2CF407B80ED247B1BC3E143B57F186333A54296FAE9097F8F7ED88E215` |

Both outputs are marker-free. The metadata contains the base column and each developer column once;
the resource contains both new captions.

## Exact package deployment proof

The package was generated from the clean merge workspace with `generate-pkg-zip` and wrapped as the
single `UsrMergeProof.gz` entry expected by Creatio application installation.

- Package archive SHA-256:
  `D357A2E6914F2826B7580FE4E3DDAF21778FEEA976E79D8B6CD3297B1EDAA601`
- Install ZIP SHA-256:
  `28E43F2CA940BAC1ABB1700D69976C66328579C9FDC0818C9B1250A1570F2F43`
- Extracted metadata and resource hashes exactly matched the merge-commit working files above.
- `install-app --check-compilation-errors true` completed successfully on A and B.
- Both configuration builds finished. The platform emitted existing optional-assembly and
  `System.Text.Json` advisory warnings, but no compilation error.
- Remote read-back on both instances returned `UsrDeveloperAText` as own `Text` and
  `UsrDeveloperBNumber` as own `Integer`, with the expected titles.

## Operational findings for reruns

- `pkg-to-db` asks the target Creatio instance to load its linked filesystem package. Running it
  from the integration clone does not deploy that clone when the instance is linked to a developer
  workspace.
- A package created as a Creatio application must be installed as an application ZIP containing
  the package `.gz`; pushing the `.gz` directly reports `Install information is empty`.
- `publish-app --app-version` mutates workspace package version manifests and is not the proof
  packaging command. Generate the package from a clean commit, assert the workspace remains clean,
  then verify the extracted archive file hashes before installation.
- Creatio EntitySchema `metadata.json` can use flat-diff syntax. Validate it with the resolver and
  platform compilation, not a generic JSON parser.

## Packaged MCP and source CLI replay

After the authorized resolver transfer, clio was built in `Release` and produced
`clio.0.0.0.nupkg` with SHA-256
`F8E282AAEF7925C01C49933FF9705BB8B06C97217A54FE23909914575A65B0C1`. Inspection proved the
package carries `Creatio.ConflictResolver.dll` for both `tools/net8.0/any` and
`tools/net10.0/any`. The package was installed into an isolated local tool directory and reported
clio `8.1.0.110`.

The real-process `CreatioArtifactMergeToolE2ETests` suite was then run with
`McpE2E__ClioProcessPath` pointing at that installed executable: 5 passed, 0 failed, 0 skipped. It
proved resident discovery, the preserved EntitySchema stage merge, the explicit BusinessProcess
`not-implemented` result, strict unknown-argument rejection, and byte-identical merge content and
report over stdio and Streamable HTTP.

After adopting CLI-first delivery, Release was rebuilt and repacked locally. The replacement local
`clio.0.0.0.nupkg` has SHA-256
`5EF934CCBC1B75906B4E55192AA665540CC465C195525A024432E6C96210D8AD`. It was installed with an
isolated NuGet config containing only the local package directory. The installed
`merge-creatio-artifact` command returned exit code 0, `status=resolved`, resolver verification
passed, no markers remained, and the content contained both `UsrDeveloperAText` and
`UsrDeveloperBNumber`. The expanded real-process suite passed 7 of 7 and proved CLI, stdio MCP, and
Streamable HTTP content/report parity plus the CLI's explicit BusinessProcess refusal. No package
was published to an external NuGet feed.

## Remaining acceptance work

- Run the remaining `unsupported`, no-write, and concurrency controls through the packaged process.
- Reset both retained instances to B0 after the packaged CLI/MCP rerun and append a sanitized final
  receipt.

## Agent-mediated true-conflict proof

On 2026-08-22, a detached proof workspace recreated a true conflict by starting from
`91351400aa3199066a6296ecf8ee6f24c6e70693` and merging
`fd7855e40fc3e37f307dbf8b89783094258fdfad`. The workspace was isolated from the already-pushed
lab `main` branch.

The real stdio MCP server received the inline Git stages through `merge-creatio-artifact`. It
returned `status=conflicts-remain`, `artifact-kind=entity-schema-metadata`, and identified the true
conflict at `UsrDeveloperAText.Body.S2`. The candidate content retained both
`UsrDeveloperAText` and `UsrDeveloperBNumber` while exposing these alternatives only inside the
conflict block:

- Developer A: `79bccffa-8c8b-4863-b376-a69d2244182b` (`Rich Text`)
- Developer B: `26cba63c-daf1-4f36-b2ea-73c0d675d90c` (`Phone Text`)

An independent merge agent then replayed the same stages through the CLI-first command under an
explicit no-write-before-decision rule. It reported the following stage blobs both before and
after inspection, proving that asking the question did not change or stage repository content:

| Artifact | Stage 1 | Stage 2 | Stage 3 |
|---|---|---|---|
| `descriptor.json` | `c256b2c8` | `d715472e` | `3e5202c9` |
| `metadata.json` | `70e3615e` | `332aacf2` | `42ec55c6` |

The agent stopped and asked exactly: `Which type should UsrDeveloperAText keep: Rich Text or Phone
Text?` The user selected `Phone Text`. Only after that answer did the agent resolve and commit the
isolated merge.

- Proof merge commit: `bbff56a2f7316680f76a7b69a27673353f2e2688`
- Proof tree: `2ee74351f17045857fe6e979f8e79b8150fb56f8`
- Parents: `91351400aa3199066a6296ecf8ee6f24c6e70693` and
  `fd7855e40fc3e37f307dbf8b89783094258fdfad`
- `UsrDeveloperAText`: exactly once, with Phone Text `S2`
- `UsrDeveloperBNumber`: exactly once
- Rejected Rich Text UId: absent
- Conflict markers: zero
- Unmerged entries: zero
- Final proof worktree status: clean
- External push: none

After normalizing line endings and the optional final newline, the proof metadata was byte-for-byte
equal to the independently completed and pushed lab `main` merge
`ad95362371e8a1d4f0a2587150001ea98bf1661a`.

This proves the intended agent boundary: MCP and CLI expose the semantic alternatives without
choosing; the agent asks the user; repository mutation occurs only after the answer; and the final
artifact retains only the selected alternative.

## Scenario 1 fixture refresh: same column, different types

On 2026-08-23 both developer workspaces began from their retained common EntitySchema containing
`UsrDeveloperAText` as Text. Using the issue-worktree clio build:

- instance A changed the existing column to `Integer`; publish and OData rebuild completed;
- instance B changed the same column to `DateTime`; Creatio wrote and published the schema metadata,
  while PostgreSQL returned `42804` because the physical text column could not be cast automatically
  to timestamp;
- `get-entity-schema-column-properties` subsequently reported `DateTime` for instance B, confirming
  the Date/Time candidate was present in Creatio schema metadata.

The generated workspace metadata was captured before cleanup:

| Candidate | Type UId | Source SHA-256 |
|---|---|---|
| Developer A, Number | `6b6b74e2-820d-490e-a017-2b73d4ccf2b0` | `B90FE18BD8867C9DFBA2C9050253D139DB69D48539D0533139B45181B6833254` |
| Developer B, Date/Time | `d21e9ef4-c064-4012-b286-fa1a8171da44` | `F5F607D68FDEAA305D2EF734D0F7004FC3278512C5DB3D086E4E72F0AF432DAE` |

Only `UsrDeveloperAText.Body.S2` differs between the normalized base and each candidate. The
automated lab now creates real commits from these fixtures, proves CLI and MCP return
`conflicts-remain` without mutation, derives the exact question `Which type should
UsrDeveloperAText keep: Number or Date/Time?`, and applies the scenario answer `Developer A wins`.
The committed merge is required to contain Number and no Date/Time alternative.

Cleanup restored instance A to RichText and instance B to PhoneNumber. Both developer workspaces
were restored clean at `6c7eb056e3d4286ba5710ec0ab776febbdc1297e` and
`fd7855e40fc3e37f307dbf8b89783094258fdfad`, respectively.

## Main merge-validation instance

On 2026-08-23 the retained lab was expanded to one Creatio instance per workspace. The dedicated
main-branch target is:

| Item | Value |
|---|---|
| Environment | `issue-1183-main` |
| URL | `https://k-krylov-nb.tscrm.com:51085` |
| Creatio | `10.1.585.0`, Studio, .NET 8.0.30, PostgreSQL |
| Workspace | `F:\Projects\Issue-Workspaces\issue-1183\main` |
| Package link | `Terrasoft.Configuration\Pkg\UsrMergeProof` points to the main workspace package |
| ClioGate | `cliogate_netcore` 2.0.0.45 |

Canonical Phase 1 deployment succeeded. Its immediate post-FSM calls encountered the known host
restart race: `pkg-to-file-system` saw disabled file design mode and compilation received IIS 503
while the server was shutting down. After `get-info` proved the host healthy, the single permitted
recovery succeeded: configuration compilation completed, followed by one successful
`pkg-to-file-system` call. Live web-host configuration then showed `fileDesignMode=true` and
`UseStaticFileContent=false`.

Canonical Phase 2 linked `UsrMergeProof` to the exact main workspace, downloaded configuration
dependencies, loaded the package into the database, restarted the host, and flushed its Redis
database. Final verification reported the package registered at version 0.1.0 and read
`UsrDeveloperAText` as `PhoneNumber`, matching the main branch state at provisioning time. The main
Git workspace remained clean.

This instance is now the only valid live target for final-merge acceptance. Developer instances A
and B remain authoring inputs and must not be used to certify an integrated result.

## Scenario 1 live three-instance acceptance

On 2026-08-23 the first conflicting-type scenario ran end to end against all three retained
workspaces and their independently connected Creatio instances.

| Role | Branch | Commit | Result |
|---|---|---|---|
| Common B0 | `main` | `99dc14461d493d63ddb98c3b30ef378888e582dc` | `UsrDeveloperAText` is Text on all three instances |
| Developer A | `scenario-1-developer-a` | `21231c4086f2c21402813a9aee936fb6a9c48d79` | Changed the existing column to Number (`Integer`) through Creatio A |
| Developer B | `scenario-1-developer-b` | `e774d8ecdee8f5ef038aea2fa73175b0422e7d55` | Changed the same column to Date/Time through Creatio B |
| Main merge | `main` | `b3b331317f81ddfb10e1d50b599af9774b77eaad` | Developer A wins; only Number remains |

The main merge has exactly two parents, Developer A followed by Developer B. Its tree is
`683d9e5412f895b33756b235e94fe6f4833a759f`. The final EntitySchema metadata blob is
`2adcf492428470651f742c6bcde35d5494bdd065`, exactly Developer A's metadata blob. This is expected:
the selected side needed no metadata rewrite, while the descriptor was semantically resolved into
blob `56632c62635cbc80605a4e71e76f900794ccf72e`.

The real Git conflict contained six index entries: stages 1, 2, and 3 for both `descriptor.json`
and `metadata.json`. Before any resolution, both the Release CLI and the real stdio MCP server
received those stage contents. Each returned:

- `status=conflicts-remain` and `artifact-kind=entity-schema-metadata`;
- resolver provenance `1.0.0+source.e65852f9521b`;
- true conflict path
  `$.Items[MetaData.Schema.D2.["c066e869-c117-4780-84bb-fa428d00315b"].{hasBody:true}].Body.S2`;
- the exact question `Which type should UsrDeveloperAText keep: Number or Date/Time?`;
- byte-identical marker content with SHA-256
  `F726ABE27B395C7534ED3A2E3CEF50C577CE76B9A6A4C954FC2B658F9E374ECA`.

After both previews, the six unmerged index entries and the working-file SHA-256 values were still
unchanged. Only then was the user's existing `Developer A wins` answer applied. One conflict marker
was replaced with its Local side, semantic verification passed, no markers or unmerged entries
remained, and the two-parent merge was committed and pushed to the retained bare origin.

The exact merge commit was then loaded from the linked main workspace into `issue-1183-main`.
`compile-configuration` completed successfully. The platform emitted pre-existing missing optional
assembly and `System.Text.Json` advisory warnings, but no compilation error.
`get-entity-schema-column-properties` reported `UsrDeveloperAText` as own `Integer` on the main
instance. Finally, a disposable OData row was created with `UsrDeveloperAText=73` and
`UsrDeveloperBNumber=42`, read back with both exact values, and deleted. The three Git workspaces
were clean at the end of the run.

Developer B's attempt also exposed a platform limitation relevant to this fixture: PostgreSQL
returned `42804` because the existing text column could not be cast automatically to timestamp.
Creatio still wrote and published the Date/Time schema metadata, so it remained a valid authentic
merge candidate. Final runtime certification deliberately occurred only on main, where the selected
Number schema compiled and the integer value round-tripped successfully.

## Scenario 2 live three-instance acceptance

On 2026-08-23 all three workspaces and instances started from Scenario 1 merge
`b3b331317f81ddfb10e1d50b599af9774b77eaad`. Canonical Phase 2 synchronization completed every
ordered stage on instances A and B, and both reported the common `UsrDeveloperAText` column as
`Integer` before either developer change.

| Role | Branch | Commit | Creatio-authored change |
|---|---|---|---|
| Developer A | `scenario-2-developer-a` | `43684f2eb825ac537988b3da88fbf40fef2aaa9b` | Added Text `UsrScenario2AAdded`; renamed the shared column to `UsrScenario2AName` |
| Developer B | `scenario-2-developer-b` | `81e9af08f9a2539064e5d5e4d39acdfedf9137b8` | Added Integer `UsrScenario2BAdded`; renamed the shared column to `UsrScenario2BName` |
| Main merge | `main` | `f776da1f613f36ce4a980969235fff3f8b2b1057` | Kept both additions and Developer A's rename only |

The real Git merge produced stages 1, 2, and 3 for the descriptor, metadata, and `en-US` resource.
The metadata stage blobs were `2adcf492428470651f742c6bcde35d5494bdd065`,
`237b46d7f98ab3dc112a9d785a20119e8329549c`, and
`0b0f22ac24dcfe3c572ceff89eae837a7de31da7`. CLI and real stdio MCP both returned
`status=conflicts-remain`, preserved both additions and both rename alternatives in candidate
content, and reported the single true conflict at the shared column's `Body.A2`. Their candidate
content was byte-identical with SHA-256
`170AF09024A94BB8EFD47BF66BF4B27DF60D60630162950EC0FED7EF49A9767F`.

Both previews left the three conflicted working files and all nine index entries unchanged. The
pre-authorized `Developer A wins` answer was then applied to the single metadata marker. The resource
union was made consistent with that choice by removing only Developer B's rejected rename caption;
Developer B's genuinely new-column caption remained. Semantic verification passed for the final
metadata and resource before commit.

Merge commit `f776da1f613f36ce4a980969235fff3f8b2b1057` has exactly the Developer A and Developer B commits
above as its parents, tree `be125740794363d678c3f6b22ade91981d51518d`, metadata blob
`814a4e06ae5d69e8dc830cd496a01422d7e6b5d3`, and resource blob
`5f31110a7a0e4755c331b8efe3e73d965f5e4e41`.

The exact merge commit was loaded into `issue-1183-main`. Schema readback returned:

- `UsrScenario2AName`: Integer;
- `UsrScenario2AAdded`: Text;
- `UsrScenario2BAdded`: Integer;
- `UsrScenario2BName`: absent.

An incremental compilation succeeded but the running OData model still exposed the old column name.
A documented full compilation also succeeded; after restarting the main web application and flushing
Redis, OData metadata exposed all three selected columns, omitted both the old and rejected names,
and accepted a disposable record. Readback returned `UsrScenario2AName=17`,
`UsrScenario2AAdded=developer-a-added`, `UsrScenario2BAdded=29`, and
`UsrDeveloperBNumber=42`. The record was deleted, and all three Git workspaces finished clean.

## Scenario 3 live three-instance acceptance

On 2026-08-23 all three roles started from Scenario 2 merge
`f776da1f613f36ce4a980969235fff3f8b2b1057`. Developer A added Text
`UsrScenario3AAdded` and deleted `UsrScenario2AName` in instance A, producing commit
`8bf5fdf20dbda7bea751c21b7a7b28dcc329a643`. Developer B added Integer
`UsrScenario3BAdded` and renamed the same shared column to `UsrScenario3BName` in instance B,
producing commit `143a1be28d5442b673ed62bc2f6f08498a5edc31`.

The real Git merge conflicted in descriptor, metadata, and `en-US` resources. The first metadata
preview correctly failed closed as `invalid-input`: the resolver reported whole-item and collection
body conflicts but the flat formatter emitted no markers for an item deleted by its local winner.
The minimal formatter repair adds selectable whole-item output and builds primitive-array choices
from the already merged union, so neither independent addition can be lost. After the repair, CLI
and real stdio MCP returned byte-identical `conflicts-remain` content with SHA-256
`A4D3AA0DC80951B70899A8FC297E9DC0DAA7D3BDEF73D0D3C309E4170FC5E064`, two true conflict paths,
and two Local/Remote marker blocks. Both previews left working files and all Git stages unchanged.

The pre-authorized Developer B choice was applied to both blocks. Semantic verification passed for
the selected metadata and resource. Merge commit
`bc63be30a0775038423555d2a9e8213ed838fa4a` has the two developer commits above as its parents,
tree `db3582c09a821fdaf5c14610e5238610e4e33f92`, metadata blob
`d9d15c60129c4bfe8cd9d145f33eb8c552a136ce`, descriptor blob
`2e04d51a560cdc2f525283e44f3cd0e63e9dd731`, and resource blob
`39fc117ed4cbc779358b6732d308245e03a0f21c`.

The exact merge was loaded into `issue-1183-main`, followed by a successful full configuration
compile, restart, and Redis flush. Schema readback and OData metadata both showed Text
`UsrScenario3AAdded`, Integer `UsrScenario3BAdded`, and Integer `UsrScenario3BName`; both reported
`UsrScenario2AName` absent. A disposable OData row round-tripped values `scenario-3-a`, `37`, `31`,
and control value `42`, then was deleted. All three retained Git workspaces finished clean.
