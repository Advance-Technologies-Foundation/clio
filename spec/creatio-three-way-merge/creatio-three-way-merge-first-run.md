# Creatio-aware three-way merge first lab run

## Scope and result

The first live run on 2026-08-22 proved the resolver engine against a real Git three-way conflict
created by two independent Creatio instances. The merge result was committed, packaged from the
exact merge commit, installed on both instances, compiled, and read back successfully.

This is not yet the packaged MCP acceptance result. The run invoked the resolver CLI directly from
the authorized source snapshot at commit `e65852f9521b2c1d288883428b3dd7ebb6fc73be` while the clio
source transfer is awaiting explicit public MIT redistribution authority. Repeat the same stage
inputs through the packaged `merge-creatio-artifact` tool before closing the feature.

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

## Remaining acceptance work

- Repeat the preserved stages through the packaged MCP tool over native stdio, `clio-run`, and HTTP.
- Run the explicit `not-implemented`, `unsupported`, `invalid-input`, no-write, size, and concurrency
  controls.
- Perform the record create/read/delete round-trip.
- Reset both retained instances to B0 after the packaged-MCP rerun and append a sanitized final
  receipt.
