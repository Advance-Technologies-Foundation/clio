# Creatio-aware three-way merge end-to-end test plan

## Proof objective

Prove that three developers can start from one common EntitySchema, change the same existing Text
column to Number and Date/Time through two developer Creatio instances, produce two real Git commits, and
have the third developer detect the semantic conflict through the packaged clio CLI and MCP. The
agent must ask which change wins; after `Developer A wins`, the merged package must install, compile,
and expose Number only on that column in a third, main-branch Creatio instance.

This is acceptance validation, not a unit-test substitute.

## Developer-local automated lab

`CreatioArtifactMergeGitLabE2ETests` recreates the Git and agent-decision portion of this plan when
selected explicitly. It creates a temporary bare origin and three clones, authors commits from the
preserved Creatio-generated Text/Number/Date-Time fixtures, invokes the real CLI and stdio MCP
processes, proves previews do not mutate Git, asks which developer wins, and completes the merge
with the user's `Developer A wins` answer. The fixtures are `[Explicit]` and
`McpE2E.Manual`; they are excluded from GitHub Actions and TeamCity and run only when a developer
selects them directly.

`CreatioArtifactMergeToolE2ETests` retains the supported independent-addition, discovery,
not-implemented, strict-binding, and transport-parity checks. Run both fixtures together using the
command in the lab README.

The three live Creatio instances remain the platform oracle used to refresh fixture provenance and
perform release acceptance when the Creatio version, serialization format, deployment path, or
supported artifact families change. Developer instances author A1 and B1; the independent main
instance alone validates the selected merge. They are not required for ordinary implementation iterations.

## Scenario 2: independent additions and competing rename

Start all three workspaces and instances from the same merged Scenario 1 commit. Developer A adds
`UsrScenario2AAdded` and renames the existing shared column to `UsrScenario2AName`. Developer B adds
`UsrScenario2BAdded` and renames that same existing column to `UsrScenario2BName`.

Required behavior:

- CLI and MCP preserve both independent additions in their candidate content;
- the only true metadata conflict is the shared column's `Body.A2` name;
- neither preview changes the worktree, index, or Git stages;
- after `Developer A wins`, metadata and resources contain Developer A's selected rename and no
  Developer B rename or orphan caption;
- both new columns and their captions remain exactly once;
- the final commit has Developer A and Developer B as its two parents;
- only the main instance certifies the result through full compilation, restart, schema readback,
  refreshed OData metadata, and record create/read/delete.

The full compile must be followed by a main-instance restart and Redis flush before the OData
round-trip. A successful schema readback alone does not prove the generated OData model stopped
serving its previous column names.

## Scenario 3: delete versus rename with independent additions

Start all three workspaces and instances from the merged Scenario 2 commit. Developer A adds Text
`UsrScenario3AAdded` and deletes the existing `UsrScenario2AName` column. Developer B adds Integer
`UsrScenario3BAdded` and renames the same existing column to `UsrScenario3BName`.

Required behavior:

- CLI and MCP return byte-identical `conflicts-remain` results and do not mutate Git;
- the agent sees selectable alternatives for both the deleted item and its collection membership;
- selecting Developer B consistently keeps `UsrScenario3BName` and removes `UsrScenario2AName`;
- both `UsrScenario3AAdded` and `UsrScenario3BAdded` survive as schema items and collection members;
- the selected flat metadata is marker-free, reparses successfully, and produces a clean two-parent merge;
- only the main instance certifies the result through full compilation, restart, Redis flush, schema
  readback, OData metadata, and a disposable create/read/delete round-trip.

The automated test must select every marker block, not merely the first one, because the item and
its ordering-collection membership are one coordinated user decision.

## Topology

Use one remote Git repository and three independent clones:

| Developer | Workspace | Branch | Creatio responsibility |
|---|---|---|---|
| Developer 1 | integration workspace | `main` / integration branch | Linked exclusively to main instance C; creates the base, performs the merge, and verifies the merged package. |
| Developer 2 | feature workspace A | `developer-a` | Linked exclusively to Creatio instance A; authors column A. |
| Developer 3 | feature workspace B | `developer-b` | Linked exclusively to Creatio instance B; authors column B. |

All three Creatio instances must have the same version and begin with the exact same committed package.
Do not create the EntitySchema independently on multiple instances because that would create different
schema UIds.

Use named branches in three independent clones rather than detached HEADs. Give each actor a
workspace-scoped Git identity and a separate clio configuration directory. The reusable setup,
execution, evidence, reset, and cleanup procedure is defined in
`creatio-three-way-merge-lab.md`.

## Test data

- Package: `UsrMergeProof`
- EntitySchema: `UsrMergeProofEntity`
- Base column: `UsrName`, text
- Existing conflict column: `UsrDeveloperAText`, text at B0
- Unrelated control column: `UsrDeveloperBNumber`, integer at B0
- Developer 2 change: `UsrDeveloperAText` from text to number
- Developer 3 change: `UsrDeveloperAText` from text to date/time

Use a dedicated disposable package, repository, records, and branches with two exclusively owned,
long-lived lab Creatio instances. Record their exact identities before mutation so reset and cleanup
are deterministic.

## Phase 1: create the common base

1. Developer 1 creates `UsrMergeProof` and `UsrMergeProofEntity` once in main instance C.
2. Synchronize that package into the integration workspace.
3. Commit and push the common base as B0.
4. Compress the exact B0 package and install it into developer instances A and B.
5. Unlock both developer copies and prove each assigned developer can edit only their instance.
6. Verify all three instances report the same package UId and EntitySchema UId.
7. Keep main instance C at B0; never deploy A1 or B1 there separately.
8. Create `developer-a` and `developer-b` from B0 in their independent clones.

Evidence: B0 commit SHA, package UId, schema UId, archive hash, successful installation in both
instances, and the explicit instance-B unlock result.

## Phase 2: create independent developer commits

Developer 2 uses Creatio instance A to change `UsrDeveloperAText` from text to number, synchronizes
the package into feature workspace A, confirms the diff, commits A1, and pushes `developer-a`.

Developer 3 uses Creatio instance B to change the same `UsrDeveloperAText` column from text to
date/time, synchronizes the package into feature workspace B, confirms the diff, commits B1, and
pushes `developer-b`.

Before merging, prove:

- A1 retains the column UId and changes only `UsrDeveloperAText.Body.S2` to Number;
- B1 retains the same column UId and changes only `UsrDeveloperAText.Body.S2` to Date/Time;
- A1 and B1 both descend directly from B0 for the scenario;
- both preserve the B0 EntitySchema UId.

No package artifact may be hand-edited to manufacture the changes.

## Phase 3: create a real Git conflict

Developer 1 fetches both branches into the integration workspace, fast-forwards the integration
branch from B0 to A1, then attempts a no-commit/no-fast-forward merge of B1. This makes the final
merge commit's two parents exactly A1 and B1.

The test fails unless Git reports a conflict and `git ls-files -u` contains stages 1, 2, and 3 for
the EntitySchema `metadata.json`. Record the commit and blob SHAs.

If the generated column names do not collide textually, repeat with two names that sort into the same
generated insertion point. The changes must still be authored through Creatio; do not edit exported
JSON solely to force a conflict.

If `descriptor.json` is conflicted, resolve it through the same CLI command first. Use the resulting
marker-free descriptor as `--descriptor-file` for `metadata.json`. If it is not conflicted, use the
working-tree descriptor after proving it retains the B0 manager and schema identity.

The integration branch must still point exactly at B0 before the fast-forward to A1. Record
`git rev-parse HEAD`, A1, and B1 before starting the merge.

## Phase 4: invoke the packaged CLI

1. Build and pack clio from the issue branch.
2. Install that package into an isolated local dotnet-tool location.
3. Extract the actual Git stage blobs to four explicit files.
4. Run the installed `clio merge-creatio-artifact` command with `--artifact-path`, `--base-file`,
   `--ours-file`, `--theirs-file`, and `--descriptor-file`.

Expected preview result:

- `status=conflicts-remain`;
- `artifact-kind=entity-schema-metadata`;
- `content` contains both Number and Date/Time alternatives for `UsrDeveloperAText.Body.S2`;
- original package and schema identities remain unchanged;
- CLI and MCP return the same report and content;
- neither preview mutates the worktree, index, or conflict stages.

The agent must ask exactly: `Which type should UsrDeveloperAText keep: Number or Date/Time?` For
scenario 1, answer `Developer A wins`; the selected resolution must retain Number and remove the
Date/Time alternative.

Then start the installed `clio mcp-server`, prove the tool is resident with the intended safety
annotations, and call it with the same bytes inline. Run stdio and HTTP MCP. CLI, stdio MCP, and HTTP
MCP must return byte-identical content and reports. Do not wrap this resident tool in `clio-run`.

## Phase 5: finish the Git merge

Only after the user answers, Developer 1 writes the selected marker-free `content`, stages it, and proves `git ls-files -u` is
empty. Run `git diff --cached --check`, scan the staged blob for conflict markers, commit the merge,
and verify the merge commit has A1 and B1 as its two parents.

The final blob must contain `UsrDeveloperAText` exactly once with the Number type UId, contain the
unrelated `UsrDeveloperBNumber` exactly once, and contain no Date/Time alternative. Record the merged
blob SHA and content SHA-256.

## Phase 6: prove the merge in Creatio

After A1 and B1 are safely pushed, use main instance C as the only verification target. Prove it is
still at B0 before deployment. If its state is uncertain, remove the test package, prove its schema
is absent, reinstall the exact B0 archive, and prove only the base column is present.

1. Compress the package from the merge commit.
2. Install it into main instance C using the issue-worktree clio build.
3. Compile and synchronize the configuration.
4. Read `UsrMergeProofEntity` back through clio and verify `UsrDeveloperAText` is Number with its
   original column UId, and `UsrDeveloperBNumber` remains unchanged.
5. Create one record with `UsrDeveloperAText = 7` and `UsrDeveloperBNumber = 42`.
6. Read the record back and verify both values.

Successful installation, compilation, schema read-back, and record round-trip prove that the merge is
valid beyond JSON formatting.

## Negative acceptance scenarios

Exercise these controls with deterministic stage fixtures through the packaged CLI, then assert MCP
returns the same domain statuses for the agent-facing contract.
The reusable real-Creatio lab owns the positive merge and one real `conflicts-remain` no-write
control; it does not recreate every negative case through Creatio.

| Scenario | Required result |
|---|---|
| Both developers change the same EntitySchema property differently | `conflicts-remain`; marker content returned; no automatic write or stage. |
| `ProcessSchemaManager` metadata or process resources | `not-implemented`; type-specific `not implemented yet` diagnostic; no result content. |
| `ProcessSchemaManager` descriptor | `not-implemented`; `process-schema-descriptor` diagnostic; no result content. |
| C# or SQL conflict | `not-implemented`; type-specific `not implemented yet` diagnostic; no result content. |
| Unknown manager | `unsupported`; no result content. |
| ClientUnit without supported markers | `unsupported`; no result content. |
| Missing or conflicted descriptor evidence | `invalid-input`; no result content. |
| Descriptor manager, schema name, or schema UId does not match any metadata input | `invalid-input`; no result content. |
| Resolver verification fails | `invalid-input`; no result content. |
| Bounded resolver capacity is occupied | `busy`; no result content; retry the identical request. |

For every non-resolved status, capture the working-tree hash and `git ls-files -u` before and after the
CLI and MCP calls and prove that clio changed neither.

## Transport and safety controls

- Include legitimate artifact fields named `Database`, `Server`, `Token`, and `Auth`, an XML namespace,
  a URI, and path-like strings. Prove all returned merge content is byte-identical across the native
  stdio and native HTTP paths.
- Feed diagnostics an absolute path, URI, host, and token-shaped text through a substituted resolver;
  prove each diagnostic is scrubbed while artifact content remains untouched.
- Invoke the same request twice and require a byte-identical response.
- Run parallel requests with distinct inputs and compare every result to its sequential baseline,
  proving the resolver has no cross-request state leakage.
- Exercise max-size parallel requests through the resolver concurrency bound and assert the agreed
  allocation/working-set ceiling, so the 4 MiB per-call limit cannot multiply without bound.
- Exceed the 4 MiB aggregate input budget and prove rejection occurs before resolver parsing.
- Produce an oversized resolver output and prove it is withheld with `status=invalid-input`.
- Assert the curated contract teaches callers to branch on `status`, never on a generic success
  convention, and contains the exact support matrix.
- Assert `tools/list` compactly names supported and recognized but not-yet-implemented types. For
  every recognized unimplemented type, assert `status=not-implemented` and a
  type-specific `Merge for <artifact-kind> is not implemented yet.` diagnostic.
- Run the external-process MCP E2E developer-locally on Windows, Linux, and macOS where available;
  these lab tests must remain excluded from GitHub Actions and TeamCity.
- Run ClioRing contract tests and Windows x64 NativeAOT publish because the tool catalog changes.

## Evidence receipt

Retain a sanitized receipt containing:

- three workspace paths and three branch names;
- three Creatio environment identities and versions;
- B0, A1, B1, and merge commit SHAs;
- stage 1, 2, and 3 blob SHAs;
- package/schema UIds and package archive hashes;
- raw CLI and MCP status/report and content SHA-256;
- `git ls-files -u` before and after;
- installation, compilation, schema read-back, and record round-trip results;
- exact cleanup results for branches, records, packages, and workspaces, plus proof that both lab
  instances were reset to the recorded B0 state.

The test is incomplete if any disposable repository/package resource remains unintentionally or
any retained lab instance is not returned to the recorded B0 state.
