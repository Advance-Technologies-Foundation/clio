---
description: clio.csproj includes ..\.sonarlint\clio\CSharp\SonarLint.xml as AdditionalFiles but .sonarlint is gitignored and never committed, so a local SonarAnalyzer pass runs with default rule parameters instead of the server's
applies-to:
  - clio/clio.csproj
  - .gitignore
date: 2026-08-19
---

**What is true** — `clio/clio.csproj` feeds `..\.sonarlint\clio\CSharp\SonarLint.xml` to the
compiler as `AdditionalFiles`. That directory is listed in `.gitignore` and is not tracked, so it is
absent from every fresh checkout. MSBuild does not fail on an `AdditionalFiles` item that does not
exist; the analyzers simply find no rule-parameter file.

**Why it is this way** — the file is produced by the Sonar tooling (connected-mode bind or the
scanner) against the SonarCloud project, so it is a generated artifact rather than source. The
project keeps the reference so that a developer who has bound the solution gets the server's rule
parameters for free.

**What breaks if you ignore it** — reproducing a SonarCloud finding locally by temporarily adding
the `SonarAnalyzer.CSharp` package gives a run configured with the analyzer defaults, not with the
project's tuned thresholds. A rule whose server parameters differ from the defaults then either
stays silent locally or fires on code the server accepts, and the conclusion "the issue is not
reproducible" is wrong. Materialize `.sonarlint` first, or verify against the SonarCloud API
instead of a local pass.
