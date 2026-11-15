# ADR: Data-binding commands keep every path inside the workspace

- **Status:** Accepted
- **Date:** 2026-11-15
- **Feature:** `data-binding-path-confinement`
- **Spec:** [spec-data-binding-path-confinement.md](../prd/spec-data-binding-path-confinement.md)

## Context

`DataBindingService` builds `<workspace>/packages/<package-name>/Data/<binding-name>` with `Path.Combine` and
checked only that the folders exist. `Path.Combine` keeps `..` segments and lets a rooted segment replace everything
before it, so both names could move the binding folder anywhere the person can write. `create-data-binding` then
creates the folders, writes three files and recursively deletes `<binding>/Localization`; the two row commands
rewrite `data.json` in any folder that holds a descriptor. Each `localizations` key became the file name
`data.<culture>.json` unchanged, so a key with separators named a JSON file elsewhere, which `add-data-binding-row`
read and replaced (the JSON options ignore unknown properties, so any JSON object qualified).

The image-content conversion had three weaker gaps: it asked whether the file exists before checking confinement,
so the error told whether a file outside the workspace exists; it compared the workspace prefix ignoring case on
every platform, which on Linux accepts a different folder; and it followed symbolic links.

These commands are MCP tools. An MCP host can sandbox the agent, but clio runs on the person's machine with the
person's permissions, so whatever a tool argument reaches, the agent reaches.

## Decision

1. **Names are plain folder names.** `DataBindingPathGuard.RequirePlainName` refuses a package or binding name with
   a separator (`/` or `\` on every platform), a drive or root, a character invalid in a Windows file name, a
   control character, a leading or trailing space, or a trailing dot (Windows drops trailing dots and spaces, so
   `Pkg.` would name `Pkg`; the rule also covers `.` and `..`). The check runs with the other argument checks,
   before any file is touched; for `create-data-binding` the effective binding name (the schema name when
   binding-name is omitted) is checked before the schema is fetched from Creatio. It is a deny-list of what changes
   the path, not an allow-list of Creatio identifiers, so existing binding folders with spaces or inner dots keep
   working.
2. **Localization keys are culture names.** `^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8}){0,4}\z` (`en-US`, `sr-Latn-RS`):
   letters, digits and hyphens only, checked while parsing `localizations`, before any write.
3. **Every path stays strictly below `<workspace>/packages` and passes through no link below the workspace root.**
   `EnsureConfined` compares full paths by their exact spelling (ordinal: on a case-sensitive volume a folder spelled
   with other letter case is a different folder, possibly a link the walk below would take for the root) and asks
   `IFileSystem.HasLinkWithin(workspace, path)`, so the `packages` folder is probed like every segment under it.
   Only the workspace root and its ancestors are trusted: they are what the caller named (on macOS the temp folder
   itself sits behind `/var -> /private/var`), and a host that confines the caller checks that name. Everything
   below it is clio's to keep in place.
4. **Check before reading, and again right before writing.** The binding folder, its three files and
   `Localization` are checked before the first read. Every file a command writes is built in memory first: the
   descriptor, `data.json`, every culture file (existing culture files are read and merged, localized values are
   converted, an image path among them may be refused). Then the whole set, culture files included, is checked once
   more and written. So a refusal never leaves a half-applied row, and a link swapped in during a schema fetch or an
   image read is still refused; only the last few system calls before each write are unchecked.
5. **Image content.** Confinement is decided first, from the path alone, so the answer never depends on whether a
   file outside the workspace exists. A value outside the workspace that is valid base64 stays image data (JPEG data
   starts with `/9j/`, a rooted path); any other is refused with one message. Inside the workspace a path through a
   link is refused before the file's existence is looked at, for the same reason. Without a workspace path no file
   is read at all; only primary-key conversions pass none.
6. **Windows reparse points.** `FileSystem.IsLink` reads the reparse tag when a reparse point has no readable
   target: only a symbolic link or a junction counts as a link (a dangling or cyclic one included). A cloud-files
   placeholder (OneDrive), a dedup or a ProjFS entry is an ordinary entry, so a workspace under a synced folder is
   not refused. An unreadable tag still fails closed. This also narrows the nuget2dll check, which shares the helper.
7. **Refusals** are `InvalidOperationException`s: exit code 1 with an error message on the CLI, a normal command
   envelope over MCP. The MCP `workspace-path` check also refuses a network share written as `//host/share`.

## Alternatives considered

- **Follow links and confine the real path** (as `OutputPathConfinement` does for `--output-file`). A link that
  points inside the workspace when checked can be repointed before the write, and a binding has no reason to be a
  link; refusing links is simpler.
- **Let `packages` be a link, as nuget2dll does** (a workspace whose packages live on another drive). The caller who
  names the workspace can plant that link too, and then every check below it passes while the writes, the recursive
  `Localization` delete and the image reads land wherever the link points. Telling the person's own CLI call apart
  from an MCP call is not reliable inside one shared service (the HTTP MCP host and direct callers do not set the
  MCP-server flag), so the stricter rule applies to every caller.
- **An allow-list such as `[A-Za-z0-9_]+` for names.** Safer-looking but breaks existing binding folders whose names
  carry spaces or dots; the deny-list plus the full-path check in 3 already keeps every path in place.
- **Check culture keys with `CultureInfo`.** Its answer depends on ICU and the invariant-globalization mode of the
  host; the pattern is deterministic.
- **Refuse every image value outside the workspace.** Base64 image data that starts with `/` would be refused.

## Consequences

- Inputs that are refused now: names that changed the path or alias another name on Windows, keys that are not
  culture names (Creatio does not load such files anyway), image paths outside the workspace that do not exist
  (they used to be written into `data.json` as the image value), and any link below the workspace root, a linked
  `packages` folder or a package folder linked to a shared checkout included. Such a workspace has to hold real
  folders to use these commands.
- The check and the write are not atomic: a link created inside the workspace in the last system calls before a
  write is not seen. Closing that needs descriptor-relative writes that refuse links (`openat` with `O_NOFOLLOW`), as
  `UnixConfinedFileAccess` does for new files.
- Not covered: a hard link inside the workspace to a file outside it. A write truncates the shared file, and an image
  read copies it. Telling a hard link apart needs the link count (`stat`), which .NET does not expose.
- Follow-ups outside this change: hard links (above); a FIFO given as an image path blocks the read;
  `link-from-repository-unlocked` joins server-returned package names and versions to the environment package folder
  before a recursive delete (`Link4RepoCommand`); the toolkit cursor cache folder name comes from the last segment of
  a repository URL (`SkillRepositoryResolver`); the nuget2dll writes still trust a linked `packages` folder.
