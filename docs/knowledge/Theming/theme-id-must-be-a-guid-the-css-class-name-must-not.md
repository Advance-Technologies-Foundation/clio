---
description: a Creatio theme id is a GUID (ThemeParameterValidator.TryValidateId requires Guid.TryParse for create, update and delete) while css-class-name is still a string matching ^[A-Za-z][A-Za-z0-9_-]*$ - the two are no longer interchangeable
applies-to:
  - clio/Theming/ThemeParameterValidator.cs
  - clio/Command/McpServer/Tools/CreateThemeTool.cs
  - clio.mcp.e2e/ThemingSandboxE2ETests.cs
ticket: ENG-91018
date: 2026-08-19
---

**What is true** — the platform retyped the theme `Id` to `Guid` on the create/update/delete theme
requests, so `ThemeParameterValidator.TryValidateId` rejects anything `Guid.TryParse` cannot read,
on all three verbs. The loose `^[A-Za-z0-9_-]+$` id rule that used to exist is gone; `MaxIdLength`
survives only as a display cap, applied to `theme.Id` by the `list-themes` CLI printer and by the
matching MCP tool. The `css-class-name` parameter was
NOT retyped: it is still a string and still has to match
`^[A-Za-z][A-Za-z0-9_-]*$` (`CssClassNamePattern`).

**Why it is this way** — a server-side change on the theme requests, not a clio decision. clio only
validates ahead of it so the caller gets a readable message instead of a deserialization error.

**What breaks if you ignore it** — code (and tests) that reused one value for both parameters, as the
theming e2e once did with `["id"] = themeId, ["css-class-name"] = themeId`, cannot work any more: a
GUID may start with a digit, and a CSS class name may not. The two failures land at opposite ends -
a non-GUID id is refused by the platform with
`The value '<id>' cannot be parsed as the type 'Guid'`, which reads like a clio bug, and a
GUID-shaped class name is refused locally by the class-name rule. Generate the id with
`Guid.NewGuid().ToString("D")` and keep any readable prefix on the class name.
