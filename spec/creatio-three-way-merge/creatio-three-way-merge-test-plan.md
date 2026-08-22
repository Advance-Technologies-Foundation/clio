# Creatio-aware three-way merge end-to-end test plan

## Proof objective

Prove that three developers can start from one common EntitySchema, make independent changes through
two real Creatio instances, produce two real Git commits, and have the third developer resolve the
resulting Git conflict through the packaged clio MCP server. The merged package must then install,
compile, and expose both changes in Creatio.

This is acceptance validation, not a unit-test substitute.

## Topology

Use one remote Git repository and three independent clones:

| Developer | Workspace | Branch | Creatio responsibility |
|---|---|---|---|
| Developer 1 | integration workspace | `main` / integration branch | Creates the common base, performs the merge, and verifies the merged package. |
| Developer 2 | feature workspace A | `developer-a` | Linked exclusively to Creatio instance A; authors column A. |
| Developer 3 | feature workspace B | `developer-b` | Linked exclusively to Creatio instance B; authors column B. |

The two Creatio instances must have the same version and begin with the exact same committed package.
Do not create the EntitySchema independently on both instances because that would create different
schema UIds.

## Test data

- Package: `UsrMergeProof`
- EntitySchema: `UsrMergeProofEntity`
- Base column: `UsrName`, text
- Developer 2 column: `UsrDeveloperAText`, text
- Developer 3 column: `UsrDeveloperBNumber`, integer

Use a dedicated disposable package, repository, environments, records, and branches. Record their
exact identities before mutation so cleanup is deterministic.

## Phase 1: create the common base

1. Developer 1 creates `UsrMergeProof` and `UsrMergeProofEntity` once in Creatio instance A.
2. Synchronize that package into the integration workspace.
3. Commit and push the common base as B0.
4. Compress the exact B0 package and install it into Creatio instance B.
5. Unlock the installed package on instance B and prove Developer 3 can edit it.
6. Verify both instances report the same package UId and EntitySchema UId.
7. Create `developer-a` and `developer-b` from B0 in their independent clones.

Evidence: B0 commit SHA, package UId, schema UId, archive hash, successful installation in both
instances, and the explicit instance-B unlock result.

## Phase 2: create independent developer commits

Developer 2 uses Creatio instance A to add `UsrDeveloperAText`, synchronizes the package into feature
workspace A, confirms the diff, commits A1, and pushes `developer-a`.

Developer 3 uses Creatio instance B to add `UsrDeveloperBNumber`, synchronizes the package into
feature workspace B, confirms the diff, commits B1, and pushes `developer-b`.

Before merging, prove:

- A1 contains `UsrDeveloperAText` and does not contain `UsrDeveloperBNumber`;
- B1 contains `UsrDeveloperBNumber` and does not contain `UsrDeveloperAText`;
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

If `descriptor.json` is conflicted, resolve it through the same MCP tool first. Use the resulting
marker-free descriptor as `descriptor-content` for `metadata.json`. If it is not conflicted, use the
working-tree descriptor after proving it retains the B0 manager and schema identity.

The integration branch must still point exactly at B0 before the fast-forward to A1. Record
`git rev-parse HEAD`, A1, and B1 before starting the merge.

## Phase 4: invoke the packaged MCP server

1. Build and pack clio from the issue branch.
2. Install that package into an isolated local dotnet-tool location.
3. Start its real `clio mcp-server` process over stdio.
4. Complete MCP initialization and inspect `tools/list`.
5. Prove `merge-creatio-artifact` is resident with the intended safety annotations.
6. Extract the actual Git stage blobs and call the tool directly by its resident name:
   - stage 1 -> `base-content`;
   - stage 2 -> `ours-content`;
   - stage 3 -> `theirs-content`;
   - resolved descriptor -> `descriptor-content`.

Expected result:

- `status=resolved`;
- `artifact-kind=entity-schema-metadata`;
- `support-level=semantic`;
- `can-apply-automatically=true`;
- `merged-content` contains the base column plus both independently authored columns exactly once;
- original package and schema identities remain unchanged;
- resolver verification passes and no conflict marker remains.

Run the native resident call and `clio-run` call over stdio, then the native resident call over the
real HTTP MCP host. All three results must preserve merge content byte-for-byte; this is not a
durable-handler test, because the durable unmatched-name handler is unreachable for a resident tool.

## Phase 5: finish the Git merge

Developer 1 writes only the returned `merged-content`, stages it, and proves `git ls-files -u` is
empty. Run `git diff --cached --check`, scan the staged blob for conflict markers, commit the merge,
and verify the merge commit has A1 and B1 as its two parents.

The final blob must contain both columns while neither parent contained both. Record the merged blob
SHA and content SHA-256.

## Phase 6: prove the merge in Creatio

After A1 and B1 are safely pushed, use instance A as the verification target. First remove the test
package, prove its schema is absent, reinstall the exact B0 archive, and prove only the base column is
present. This removes Developer 2's change from the target before testing the merge.

1. Compress the package from the merge commit.
2. Install it into instance A using the issue-worktree clio build.
3. Compile and synchronize the configuration.
4. Read `UsrMergeProofEntity` back through clio and verify both column names, types, and UIds.
5. Create one record with `UsrDeveloperAText = "from developer A"` and
   `UsrDeveloperBNumber = 42`.
6. Read the record back and verify both values.

Successful installation, compilation, schema read-back, and record round-trip prove that the merge is
valid beyond JSON formatting.

## Negative acceptance scenarios

Use the same real Git-stage flow for these controls:

| Scenario | Required result |
|---|---|
| Both developers change the same EntitySchema property differently | `conflicts-remain`; marker content returned; no automatic write or stage. |
| `ProcessSchemaManager` metadata or process resources | `manual-required`; no result content. |
| C# or SQL conflict | `manual-required`; no result content. |
| Unknown manager | `unsupported`; no result content. |
| ClientUnit without supported markers | fail closed; no automatically applicable content. |
| Missing or conflicted descriptor evidence | fail closed; no automatically applicable content. |
| Descriptor manager, schema name, or schema UId does not match any metadata input | `invalid-input`; no result content. |
| Resolver verification fails | `invalid-input`; no result content. |

For every non-resolved status, capture the working-tree hash and `git ls-files -u` before and after the
MCP call and prove that clio changed neither.

## Transport and safety controls

- Include legitimate artifact fields named `Database`, `Server`, `Token`, and `Auth`, an XML namespace,
  a URI, and path-like strings. Prove all returned merge content is byte-identical across the native
  stdio, `clio-run` stdio, and native HTTP paths.
- Feed diagnostics an absolute path, URI, host, and token-shaped text through a substituted resolver;
  prove each diagnostic is scrubbed while artifact content remains untouched.
- Invoke the same request twice and require a byte-identical response.
- Run parallel requests with distinct inputs and compare every result to its sequential baseline,
  proving the resolver has no cross-request state leakage.
- Exceed the 4 MiB aggregate input budget and prove rejection occurs before resolver parsing.
- Produce an oversized resolver output and prove it is withheld with `status=invalid-input`.
- Assert the curated contract teaches callers to branch on `status`, never on a generic success
  convention, and contains the exact support matrix.
- Run the external-process MCP E2E on Windows, Linux, and macOS-capable CI agents where available.
- Run ClioRing contract tests and Windows x64 NativeAOT publish because the tool catalog changes.

## Evidence receipt

Retain a sanitized receipt containing:

- three workspace paths and three branch names;
- two Creatio environment identities and versions;
- B0, A1, B1, and merge commit SHAs;
- stage 1, 2, and 3 blob SHAs;
- package/schema UIds and package archive hashes;
- raw MCP status/report and merged-content SHA-256;
- `git ls-files -u` before and after;
- installation, compilation, schema read-back, and record round-trip results;
- exact cleanup results for branches, records, packages, workspaces, and both disposable instances.

The test is incomplete if any disposable environment or repository resource remains unintentionally.
