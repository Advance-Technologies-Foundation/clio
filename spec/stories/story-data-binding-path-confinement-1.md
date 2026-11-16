# Data-binding commands keep every path inside the workspace

Status: review
Spec: [spec-data-binding-path-confinement.md](../prd/spec-data-binding-path-confinement.md)
Design: [adr-data-binding-path-confinement.md](../adr/adr-data-binding-path-confinement.md)
Test plan: [tp-data-binding-path-confinement.md](../test-plans/tp-data-binding-path-confinement.md)

As a person whose MCP host lets an agent edit data bindings, I need clio's data-binding tools to read, write and
delete only inside the workspace I named, because clio runs outside the agent's sandbox.

Acceptance:
- `create-data-binding`, `add-data-binding-row` and `remove-data-binding-row` refuse a package or binding name that
  is not a plain folder name (for create, the schema name when binding-name is omitted), before any file is touched
  and before any Creatio request.
- A `localizations` key that is not a culture name is refused before any write.
- A path that leaves `<workspace>/packages` or passes through a link below the workspace root (`packages`
  included) is refused before any read or write; a workspace under a linked path (the macOS temp folder) works.
- Every file is built before the first write, so a refusal (an image value in `localizations` included) leaves no
  half-applied row.
- An image path outside the workspace, or through a link inside it, is refused the same way whether or not the file
  exists; valid base64 stays image data; the prefix check compares the exact spelling of the workspace path.
- Valid bindings, rows and localizations behave as before.
