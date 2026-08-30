# Creatio three-way merge lab

This lab preserves the exact EntitySchema Git stages produced by the first live run and now retains
three isolated Creatio instances, one per Git workspace. It supports deterministic CLI and MCP regression testing while the full live acceptance
run remains documented in
[`spec/creatio-three-way-merge/creatio-three-way-merge-lab.md`](../../spec/creatio-three-way-merge/creatio-three-way-merge-lab.md).

## Actors in the live lab

| Actor | Branch/workspace | Creatio instance | Responsibility |
|---|---|---|---|
| merge agent | `main` | main instance only | merges the two pretend-human commits and validates the selected result |
| developer A | `developer-a` | instance A only | authors scenario A changes from the common base |
| developer B | `developer-b` | instance B only | authors scenario B changes from the common base |

All three branches and all three instances start from the same Creatio-authored base commit. The two feature changes are made
through separate Creatio instances and synchronized into separate Git workspaces. The merge agent
uses the real stage 1/2/3 blobs and calls `merge-creatio-artifact`; clio itself never reads or writes
the repository.

The main instance is the only deployment target for a completed merge. Never validate the final
artifact in instance A or B: each contains one developer's pre-merge state and is therefore not an
independent verdict on the integration result.

## Preserved fixture

`fixtures/entity-schema` contains the exact metadata stage contents and resolved sibling descriptor
from the first run:

| File | SHA-256 |
|---|---|
| `base-metadata.json` | `446A87F7296E654BF99B01BA04E1F77AF3B697C180E4215DFDF3CE003ECEC18F` |
| `ours-metadata.json` | `E178E83A69B27D49B2EB55F27B471D4617905DAAA93ECF7189255FD51752DE12` |
| `theirs-metadata.json` | `B63864CA1BF292B17E01C06491CD7C7E53B8D36080ABC7461401789D4EED382B` |
| `descriptor.json` | `8AA1E99B45F37BF74C1CE43F7539B878DD284C3C7F840E0F8E939181B31F05B6` |

The real CLI reads these files and returns verified, marker-free `entity-schema-metadata` containing
both developer columns:

```powershell
dotnet clio/bin/Debug/net10.0/clio.dll merge-creatio-artifact `
  --artifact-path packages/UsrMergeProof/Schemas/UsrMergeProof/metadata.json `
  --base-file lab/creatio-three-way-merge/fixtures/entity-schema/base-metadata.json `
  --ours-file lab/creatio-three-way-merge/fixtures/entity-schema/ours-metadata.json `
  --theirs-file lab/creatio-three-way-merge/fixtures/entity-schema/theirs-metadata.json `
  --descriptor-file lab/creatio-three-way-merge/fixtures/entity-schema/descriptor.json
```

The real-process MCP E2E then proves the agent-facing surface returns the same merge and that
BusinessProcess metadata returns the explicit `not-implemented` response.

## Automated three-workspace conflict suite

`CreatioArtifactMergeGitLabE2ETests` is the explicit developer-local regression loop. It performs the complete Git
and agent-facing part of the lab without using or modifying a developer's retained workspaces.
Scenario 1:

1. creates a temporary bare origin;
2. creates independent `main`, `developer-a`, and `developer-b` clones;
3. commits the common Creatio-authored EntitySchema fixture;
4. commits Number on developer A and Date/Time on developer B;
5. creates a real metadata Git conflict on the existing column's type;
6. invokes the real CLI and stdio MCP processes with stages 1, 2, and 3;
7. proves both previews leave Git byte-for-byte unchanged;
8. derives the exact user question from the returned alternatives;
9. applies the user's `Developer A wins` answer;
10. proves the two-parent merge retains both columns, Number only on `UsrDeveloperAText`, no markers, no
    unmerged entries, and a clean integration workspace.

Scenario 2 begins from the merged Scenario 1 schema. Developer A adds `UsrScenario2AAdded` and
renames the shared column to `UsrScenario2AName`; Developer B adds `UsrScenario2BAdded` and renames
the same shared column to `UsrScenario2BName`. The automated lab proves CLI and MCP expose one true
`Body.A2` conflict without mutation. After the pre-authorized `Developer A wins` answer, it proves
the two-parent merge contains both additions exactly once, contains Developer A's rename, and does
not contain Developer B's rejected rename.

Scenario 3 begins from the merged Scenario 2 schema. Developer A adds `UsrScenario3AAdded` and
deletes the shared `UsrScenario2AName` column; Developer B adds `UsrScenario3BAdded` and renames
that shared column to `UsrScenario3BName`. CLI and MCP expose the coordinated item and collection
membership conflicts without mutation. After selecting Developer B in both conflict blocks, the
automated lab proves both independent additions remain, the renamed shared column remains, the old
name is absent, and the result is a clean two-parent merge.

The common, Number, and Date/Time files under `fixtures/entity-schema-conflict` were captured
from the retained developer instances. They are input provenance, not handwritten mock schema
shapes.

| Conflict fixture | Checked-in SHA-256 |
|---|---|
| `base-descriptor.json` | `29F9117E09FA710A45B053C9975E06B4E9BEC5363A248D4F3C41F04D9864837E` |
| `base-metadata.json` | `07BEA678888CE81FC5D63ED3522B1BC91B0AAF9F41479983452359CCA0DB59BD` |
| `number-metadata.json` | `4C510A161D18111BA1C7EC8273C0079758A2661DBE649057243DA183311B8F4B` |
| `date-time-metadata.json` | `05E2ECA05DDDD956228647C793C3835A1CF28CD89B65826DC24768CBE00991FA` |

`fixtures/entity-schema-rename-conflict` contains the Creatio-authored Scenario 2 metadata:

| Rename fixture | Checked-in SHA-256 |
|---|---|
| `base-descriptor.json` | `B9081FF0BB736CA46F29B3C019315260456D0BC985E208C51338E056DB36E1A1` |
| `base-metadata.json` | `4C510A161D18111BA1C7EC8273C0079758A2661DBE649057243DA183311B8F4B` |
| `developer-a-metadata.json` | `617F8DE282DA35A39AE32D2C6785B7A53B024CEFD85F4126C27B3F2D8BBF75D1` |
| `developer-b-metadata.json` | `5BF9DEC331D7757E9EF324242F4CF0F9C2491876CD519BE343C675B9BEF784A7` |

`fixtures/entity-schema-delete-rename-conflict` contains the Creatio-authored Scenario 3 metadata:

| Delete/rename fixture | Checked-in SHA-256 |
|---|---|
| `base-descriptor.json` | `5007B5C58F1F44EDFBCC45D5A90DAD8060957D1B092AC581BE13D2ECF83F6700` |
| `base-metadata.json` | `658D6EC7C5AE22936A327CBA24882974A9E80823A38CD0C3D40B0334CC1C1FA7` |
| `developer-a-metadata.json` | `7930AE8A7BE9F540AB451DDF07F2EE3F7EB783DDAB08CE3230E0E3F5FB0BD947` |
| `developer-b-metadata.json` | `BB1328D0FAB16F79B20098304F447E7CFC8B3B336745715DBD15D328950B50E3` |

The Number projection applied successfully in instance A. Creatio also generated the Date/Time
projection in workspace B, but PostgreSQL rejected applying Text to timestamp with error `42804`.
The fixture deliberately preserves that Creatio-generated candidate because this lab tests Git
merge decisions, not database type-conversion compatibility.

The source workspace files were captured on 2026-08-23 before normalization. Their SHA-256 values
were `B90FE18BD8867C9DFBA2C9050253D139DB69D48539D0533139B45181B6833254` for Number and
`F5F607D68FDEAA305D2EF734D0F7004FC3278512C5DB3D086E4E72F0AF432DAE` for Date/Time. Checked-in
hashes differ only because repository fixtures use normalized line endings and a final newline.

The merge fixtures are developer-local and never run automatically in GitHub Actions or TeamCity.
Run them explicitly from the repository root:

```powershell
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Release -f net10.0 `
  --filter "FullyQualifiedName~CreatioArtifactMergeGitLabE2ETests|FullyQualifiedName~CreatioArtifactMergeToolE2ETests"
```

No Creatio instance, environment credentials, pre-existing Git repository, or manual conflict
editing is required for this command. The fixtures use an isolated temporary clio home, and
temporary repositories are deleted by fixture teardown.

The retained three-instance lab is now a provenance and platform-compatibility refresh path rather
than the everyday test loop. Re-run it when the Creatio version, serialization format, or supported
artifact family changes, then replace fixtures only with newly captured Creatio-authored files and
record their hashes in the run receipt.

Every environment-backed scenario must finish by deploying the selected main-branch package only
to `issue-1183-main`, compiling there, reading the schema back there, and exercising the merged
column values there. Git assertions alone complete the hermetic regression tier, not live acceptance.

To prove a locally installed package rather than the repository build, set
`McpE2E__ClioProcessPath` to that package's `clio` executable before running the same test. The
fixture honors an explicit path and otherwise resolves the fresh repository output.
