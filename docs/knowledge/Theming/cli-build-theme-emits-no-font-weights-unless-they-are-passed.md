---
description: clio build-theme with a custom font and no --font-weights emits family=Inter with no wght segment, while the MCP theme tools emit wght@400;500;600 - FontImportBuilder.DefaultFontWeights applies only to a null weight list and the CLI parser supplies an empty one
applies-to:
  - clio/Theming/FontImportBuilder.cs
  - clio/Command/Theming/BuildThemeCommand.cs
  - clio/help/en/build-theme.txt
ticket: ENG-93989
date: 2026-08-19
---

**What is true** — `FontImportBuilder.BuildFamilyParam` substitutes `DefaultFontWeights`
(`400, 500, 600`) only when `font.Weights` is `null`; an empty list is honoured as "no weights" and the
family is emitted bare. `BuildThemeOptions.FontWeights` is an `IEnumerable<int>` bound by
CommandLineParser, which leaves an unspecified sequence option as an empty sequence rather than `null`.
The MCP tools bind `int[]? FontWeights` on `ThemeBrandArgs`, which really is `null` when omitted.
So the two surfaces disagree, and the CLI disagrees with its own help text, which documents
`defaults to 400,500,600`. Verified on this tree:

```
dotnet clio/bin/Debug/net8.0/clio.dll build-theme --primary "#004fd6" \
  --css-class-name Probe --heading-font "Inter" --output /tmp/probe.css
# @import url('https://fonts.googleapis.com/css2?family=Inter&display=swap');
```

**Why it is this way** — nobody chose it. It is the interaction of a null-only default in the builder
with a parser that materializes empty sequences, and no test covers the omitted-weights CLI path.

**What breaks if you ignore it** — a CLI-built and an MCP-built theme with identical arguments produce
different CSS, so byte comparison between the two is not a valid identity check, and a CLI-built theme
loads only the family's regular face. Reproduce a font-weight report on the same surface it was
reported on before concluding anything.
