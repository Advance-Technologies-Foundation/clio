---
description: bash 3.2 (still /bin/bash on macOS) cannot source a process substitution - `source <(cmd)` exits 0 and defines nothing
applies-to:
  - clio/Command/Completion/ShellCompletionScripts.cs
  - README.md
date: 2026-10-17
---

**What is true** — `source <(cmd)` on bash 3.2 defines nothing and exits 0. Neither does
`. /dev/fd/63`, which reports `Bad file descriptor`. `eval "$(cmd)"` works, and zsh's
`source <(cmd)` works. Verified on GNU bash 3.2.57 (arm64 macOS) against a script that ends with a
newline (`od -c` confirmed the terminator), so this is not a truncated-last-line artefact.

**Why it is this way** — bash's `source` builtin needs a seekable file for the argument it is
given, and a process substitution is a non-seekable `/dev/fd` pipe. `cat <(cmd)` is fine, which is
why the failure does not look like a process-substitution problem: only the builtin is affected.
zsh's process substitution is a real file, so it does not hit this.

**What breaks if you ignore it** — the install line `source <(clio completion bash)` prints no
error and loads no completion, so a user pasting it into `~/.bashrc` believes tab completion is
installed and it never is. The failure is invisible on any bash newer than 3.2, so it reproduces
only on the shell macOS still ships as `/bin/bash`. The bash install line must therefore be
`eval "$(clio completion bash)"`; the zsh line keeps the idiomatic `source <(...)`. This is why
`BashScript_ShouldDocumentEvalInstall` rejects a `source <(` install line in the bash script.
