# Data-binding commands: keep every path inside the workspace

- Problem: `create-data-binding`, `add-data-binding-row` and `remove-data-binding-row` (CLI and MCP) join
  `package-name` and `binding-name` to the workspace path without a check, so `..` or a rooted value moves the
  binding folder anywhere: the commands then create folders, write `descriptor.json`, `data.json` and `filter.json`,
  and recursively delete `<binding>/Localization` there. Each `localizations` key becomes a file name
  (`data.<culture>.json`), so a key with separators names a JSON file elsewhere, which `add-data-binding-row` reads
  and replaces. The image-content file check looks at existence before confinement (the error tells whether a file
  outside the workspace exists), compares the prefix without regard to letter case on every platform, and follows
  symbolic links. `create-data-binding` with `SysSettings` uses an offline template, so none of this needs Creatio.
- Why it matters: an MCP host may sandbox the agent that calls clio, but clio itself runs on the person's machine
  with the person's permissions. An argument that leaves the workspace lets the caller change or read files the
  sandbox would refuse.
- Outcome: the three commands refuse, before any read, write or delete:
  - a package or binding name (the schema name when binding-name is omitted) that is not a plain folder name;
  - a localization key that is not a culture name;
  - a path that leaves `<workspace>/packages` or passes through a link below the workspace root, `packages`
    included;
  - an image path outside the workspace, the same way whether or not the file exists (valid base64 stays data);
  - an image path through a link.
  Every file a command writes is built first, so a refusal never leaves a half-applied row.
- Unchanged: valid bindings, base64 image values, a workspace under a linked path such as the macOS temp folder.
- Changed on purpose: a workspace whose `packages` folder or a package folder is a link is refused by these
  commands.
- Proof: unit tests for every refusal (mock file system, links included); `clio.mcp.e2e` NoEnvironment cases over
  the real stdio server through the offline SysSettings and SysModule templates.
- Design: [adr-data-binding-path-confinement.md](../adr/adr-data-binding-path-confinement.md).
