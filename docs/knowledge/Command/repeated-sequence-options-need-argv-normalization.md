---
description: CommandLineSDK rejects repeated sequence option names with RepeatedOptionError, so create-entity-schema normalizes repeated --column groups before parsing
applies-to:
  - clio/Program.cs
  - clio/Command/CreateEntitySchemaCommand.cs
date: 2026-09-11
---

**What is true** — CommandLineSDK rejects multiple occurrences of an option name even
when its target property is `IEnumerable<string>`. A single occurrence followed by
multiple value tokens works. Reproduced with `create-entity-schema --column A:Text
--column B:Integer`.

**Why it is this way** — the parser's repeated-option validation does not exempt
sequence properties. Clio normalizes complete repeated column groups at the argv
boundary; it leaves malformed groups intact for parser rejection.

**What breaks if you ignore it** — declaring a sequence property or changing its
separator cannot make the documented repeated-flag syntax work. Splitting values
on punctuation instead corrupts JSON captions and defaults. Preserve each token.
