Current architecture: archive APIs now belong to the runtime-owned PrimitiveContracts library; the historical host upgrades below motivated [the boundary correction](../primitive-contract-boundary.md).

# Package archive migration in progress

The archive port supports single and multiple package compression, ZIP containers, directory batches, branch layouts, package/workspace `clioignore` handling, extraction and explicit overwrite. It remains part of the larger Clio 8 parity effort; compatibility differences and validation limits are listed below.

## Contract decision

Text-file reading/writing cannot implement binary archives. `IArchivePrimitive` supplies regular-file enumeration and bounded Creatio gzip packing/extraction. Composition selects package contents and destination naming; Primitives owns streaming and filesystem publication. No archive, package or path-policy knowledge is added to Core.

The single-archive capability introduced Contracts ABI **10.2.0.0**; batch APIs advance it to **10.3.0.0**. A new host build is required for the new shared API. Previously packaged preview 17/runtime 10.5.0.0 retain ABI 10.2.0.0, and preview 15/runtime 10.3.0.0 retain ABI 10.1.0.0. Do not overwrite those releases or claim an older host can dynamically gain a new shared API. Same-ABI runtime replacements remain the update mechanism after the host upgrade.

## Implemented path

```shell
clio compress ./Example --destination-path ./Example.gz --skip-pdb
clio extract ./Example.gz --destination-path ./output
clio extract ./Example.gz --destination-path ./output --overwrite
clio compress ./packages --packages First,Second -d ./packages.zip
clio extract ./packages.zip -d ./output
clio extract ./archive-directory -d ./output --overwrite
```

The canonical operations are `generate-pkg-zip` and `extract-pkg-zip`. `-d` abbreviates destination; `-s` skips PDB files for compression; `-p` selects comma-separated package names. Legacy hidden `--DestinationPath`, `--SkipPdb` and `--Packages` spellings remain accepted. Extraction also accepts `unzip`. The legacy `comp-pkg` alias conflicts with `compile-package` and is not assigned here until that ambiguity is resolved across the full command inventory.

Compression requires a package directory with `descriptor.json`. It selects that file and files below Assemblies, Bin, Data, Files, Resources, Schemas and SqlScripts. The destination defaults to the package path plus `.gz`. Existing archive files are replaced only after the new archive is complete. When `branches` exists, it must contain exactly one directory, whose contents become the package root. Empty directories count when detecting an ambiguous branch layout. Other root development directories are not traversed.

Ignore rules apply in this order: workspace `../../.clio/clioignore` relative to the original package directory, package-root `clioignore`, then nested content-directory files from shallowest to deepest. Paths are package-relative and nested patterns remain scoped to their directory. Later rules can negate earlier file rules, but cannot restore files below an excluded parent directory. Nested ignore files remain package content unless excluded themselves. Rules inside an excluded directory are not loaded. Selected content cannot traverse links, including links within a content directory that might otherwise be excluded by a rule.

This uses the small [Ignore](https://github.com/goelhardik/ignore) parser, version 0.2.1, already used by legacy Clio; it has no package dependencies. Composition owns rule precedence and scope. Legacy bugs that duplicated entries or repeatedly read the first nested ignore file are deliberately not reproduced.

With `--packages`, the supplied source directory contains the named packages. If omitted, it defaults to the process working directory. Package names must be unique single path components. Each package gets its own ignore matcher; rules cannot leak into the next package. The output is a ZIP with one root `<name>.gz` entry per package. Without `-d`, its name is `packages_yy.MM.dd_hh.mm.ss.zip`, matching the legacy local-clock format. Publication replaces the output only after the complete ZIP exists.

Extraction defaults to the process working directory and requires an existing destination parent. A missing or inaccessible output parent is `archive-destination-unavailable`, distinct from a missing source. A gzip creates a child named after the archive; a ZIP selects its root `.gz` entries; a directory input selects its immediate `.gz` files. Other ZIP entries are ignored only after passing portable-name validation. This includes ignored metadata: names such as `aux.txt`, trailing spaces, traversal and backslash-terminated directory markers reject the container instead of being silently skipped. Ordinary nested gzip files and metadata are ignored, matching legacy selection. Duplicate package names are refused. If a file argument ends in neither `.gz` nor `.zip`, `.gz` is appended, including after a version-like suffix. Empty batches fail explicitly.

Existing child directories are refused unless `--overwrite` is supplied. That flag replaces entire package directories, removing stale files. The MCP `execute` operation accepts the same `overwrite` Boolean and never prompts. This intentionally replaces the old interactive prompt with an explicit automation-friendly choice.

## Wire format and publication

Legacy `clio/Common/CompressionUtilities.cs` at master `11293657cb326cb3b488c891ca9e973b5aea8c14` writes gzip-compressed records: little-endian Int32 UTF-16 character count, UTF-16LE path bytes, little-endian Int32 content byte count, then content. It is not tar. Empty files are valid. The new primitive uses bounded streaming instead of expanding the complete archive in memory.

The primitive rejects traversal, absolute/drive/stream paths, reserved portable path names, links/reparse points within content or at publication targets, duplicates and file/directory collisions. Trusted caller-selected parent aliases are resolved before I/O, allowing developer junctions and OS locations such as macOS `/var`. Returned destination paths identify the resolved location. It validates content lengths and the gzip size trailer before publishing extraction staging. Default limits are 100,000 files, 20 GiB total file content and 32,767 characters per path; the primitive contract accepts explicit smaller/larger content limits. Each file remains limited by the format's signed Int32 size. Extracted Unix trees start with a private 0700 staging root. Windows inherits parent permissions. No cross-process coordination or crash-durability guarantee is claimed.

Explicit overwrite leaves the destination untouched during decoding. After validation and the final cancellation check, the primitive renames the old directory to a random sibling backup and publishes the staged directory. Publication failure attempts to restore the backup. If restoration also fails, the workflow returns `archive-recovery-required` with the original destination and backup location; the backup is not deleted. Successful publication remains successful when backup cleanup fails: its result includes `retainedBackup` for manual cleanup. Normal results include destination, file count and uncompressed content bytes. Cleanup of failed staging is best effort and cannot mask the original failure.

Batches decode and validate **every** selected gzip before publishing any package. Content file-count and byte limits apply to the whole batch. Additional defaults are 1,000 container entries/archives, 20 GiB container input/payload bytes and 16 MiB of ZIP metadata reads. A bounded stream limits central-directory parsing before the platform ZIP reader materializes its entry list; count checks alone would occur too late. Packing streams one temporary gzip at a time into the ZIP; extraction uses bounded temporary gzip files to allow trailer validation without holding complete packages in memory.

The metadata assumption was checked against [.NET 10 ZipArchive source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipArchive.cs): the entry list starts empty, central-directory reads use a small buffer, and entries are added after each parsed header. It does not reserve the list from an untrusted declared entry count. A 300-entry fixture with long names verifies the metadata limit before selection, independently of the count limit.

Once all packages are prepared, publication proceeds synchronously and does not observe cancellation between rename steps. This is not a transaction across package directories: a later filesystem failure can leave earlier packages published. The result's `AcceptedSteps` and `packages` payload preserve those completed destinations. `failedDestination` identifies where publication stopped, with `backup` if restoration failed. Later unpublished staging is cleaned. Do not automatically retry a partially published batch.

Tests use an independent binary reader/writer, not just new-code round trips. Composition tests exercise real Core and primitives, package selection, PDB exclusion, hierarchical ignore precedence and branch ambiguity. Primitive tests verify malformed names, collisions, corrupt/truncated data, cancellation and preservation of existing content on failed overwrite. Product tests exercise CLI aliases and the stable MCP discovery/execute tools, including refusal and explicit overwrite in the same MCP session. Filesystem interruption during the two rename steps is not fault-injected; crash recovery remains outside the contract.

The broader suite passed 265 tests before review corrections (`artifacts/archive-layer-tests.log`). After corrections, all 40 primitive tests, 129 Composition tests and two archive product tests pass. Those product tests also pass against locally installed preview 17, both with its bundled implementation and the separately published runtime 10.5.0.0 (`artifacts/archive-final-installed-tests.log`, `artifacts/archive-final-runtime-tests.log`). The payload includes the ignore parser. Neither tool nor runtime was published externally.

Claude review `rev_8470141003374af1` found no P1 blocker. Its P2 parent-link portability finding was accepted and corrected with a real linked-parent test. P3 not-found errors and ignore/overwrite coverage gaps were also addressed. The review is a read-only second opinion; the subsequent corrections were verified locally, not submitted for a redundant full review. Windows execution is proven; no macOS or Linux run is claimed.

The batch slice passed 285 cases in the full solution run; two update cases failed only during temporary-cache cleanup on Windows. Cleanup now also handles access-denied file locks, and all nine rejected-update cases pass on rerun. Logs: `artifacts/archive-batch-tests.log` and `artifacts/archive-batch-update-recheck.log`. A subsequent Windows test locks a later destination file and proves that the earlier package is published, its receipt is retained, and the blocked package's original files survive. CLI/MCP coverage checks those partial receipts as well.

Claude review `rev_a8cb4e37b0384573` found no P1/P2 defects. Its P3 destination-error finding was corrected and tested, and the portable-name policy was clarified. Additional proofs now cover real Windows partial publication through primitives and both adapters, malformed ZIP length declarations, a larger central directory, and cumulative directory-input bytes. All 58 primitive tests and 136 Composition tests pass after these additions. The `comp-pkg` collision and legacy text/prompt differences remain explicit parity decisions; no new archive capability is counted as a new top-level command.
