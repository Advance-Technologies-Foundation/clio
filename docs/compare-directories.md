# Compare directories

`compare-directories` compares file content in two local directory trees without modifying either tree. It uses SHA-256 and reports relative file paths:

```shell
clio compare-directories --left ./before --right ./after
clio --execute compare-directories --local '{"left":"./before","right":"./after"}'
```

The MCP `execute` tool takes `operation: "compare-directories"` and `arguments: { "left": "./before", "right": "./after" }`. Existing discovery (`--list`, command `--help`, and MCP `list-operations`) describes the two required string arguments. No Creatio environment is required.

The successful result has `Accepted: true`, `Code: "completed"` and this payload (even when differences exist):

```json
{"added":["new.txt"],"removed":["old.txt"],"changed":["nested/content.bin"]}
```

`added` means present only on the right; `removed` means present only on the left; `changed` means the same relative path has different SHA-256 digests. Unchanged files, empty directories and metadata are omitted. Matching and output ordering are ordinal and case-sensitive on all platforms. Actual directory separators become `/`; literal backslashes in Unix filenames remain literal. Relative and absolute inputs, identical roots and overlapping roots are allowed.

Only stable trees containing ordinary files and directories are supported. Unix special entries (FIFOs, devices and sockets) are unsupported: they are not detected or safely rejected and may block while opening a file before cancellation is observed. Both trees must remain stable during comparison. The operation reads each file sequentially using the existing streaming file-hash capability. It does not create an atomic snapshot, follow symbolic links or synchronize files. Reparse points on a root, its ancestors or any encountered file/directory cause the whole operation to fail. Link checks are not a sandbox against concurrent filesystem mutation. Memory grows with the number of files and paths, while file contents are streamed.

| Code | Meaning |
|---|---|
| `invalid-arguments` | A required path is absent, blank, the wrong type or invalid for the platform. |
| `directory-not-found` | A root is missing, a root is a regular file, or a directory disappears during enumeration. |
| `symbolic-link-not-supported` | A checked path is a symbolic link or another reparse point. |
| `directory-access-denied` | A directory/file read was denied. |
| `directory-read-failed` | Another filesystem read failed. |

Failures return no partial comparison payload. Cancellation uses the existing adapter/Composition cancellation behavior; it never returns a successful partial diff. No writes, deletes, timestamps, ACL changes, or remote calls are requested.

Implementation: runtime-owned `directory-snapshot` capability performs filesystem inspection and uses `IFileHashPrimitive`; Composition compares the two results. Core, resident Contracts and generic adapter sources are unchanged.
