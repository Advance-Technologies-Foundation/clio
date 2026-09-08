---
description: the classic process designer opens at /0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId> and nothing else resolves - a guessed shell hash route (ProcessSchemaDesigner, SectionModuleV2/ProcessLibrarySectionV2) leaves the SPA stuck on the splash screen for the rest of the session
applies-to:
  - spec/eng-91853-gateways-and-flows/
ticket: ENG-91853
date: 2026-09-08
---

**What is true** — to look at a process on a .NET Framework stand, the URL is

```
http://<stand>/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>
```

The `schemaUId` is what `create-business-process` returns, and it is also the `VwProcessLib` record
`Id` — the two are the same value, so either one works. The route that gets you there through the UI
is `#CardModuleV2/VwProcessLibPageV2/edit/<schemaUId>` (the process card) followed by its **Open in
designer** button, which opens the designer in a NEW TAB at the URL above.

Two shapes that look right and are not: `#ProcessSchemaDesigner/<uid>` and
`#SectionModuleV2/ProcessLibrarySectionV2/`. `ProcessSchemaDesigner` is a real name — it is the
designer's own client class and its resource namespace — but it is not a shell route, and there is no
`ProcessLibrarySectionV2` client schema at all (the section schema is `VwProcessLibSection`).

**A guessed route does not fail visibly. It wedges the shell.** RequireJS answers
`Error: Script error for "ProcessLibrarySectionV2"` in the console and the page sits on the Creatio
splash — and it stays there for `#Desktop` too, so the next few navigations look broken as well. A
full reload of `/0/Shell/` recovers it.

**Why it is this way** — the shell resolves a hash route by asking RequireJS for a module of that
name, and a module that does not exist is a load failure rather than a routing miss, so nothing routes
back to a 404 page. The designer itself is not a shell route: it is a separate `ViewModule.aspx` view
model selected by the `?vm=` query parameter, which is why no amount of hash guessing reaches it.

**What breaks if you ignore it** — you spend the visual-verification step debugging the browser
instead of looking at the diagram, and each wrong guess costs a wedged SPA and a reload. Worse, the
wedge is easy to misread as the stand being down or the change having broken the client, because the
splash screen is exactly what a failing app looks like.

**Cheaper than a screenshot, when the question is only "is the text there":** the designer renders a
connector label as a `div.foreign-text` inside its canvas SVG, so

```js
[...document.querySelectorAll('div.foreign-text')].map(e => e.textContent.trim()).filter(Boolean)
```

returns every element caption and flow label on the diagram. That works regardless of viewport size,
which matters because the in-app browser blocks the app's own `//core/...` bootstrap loader
(`ERR_BLOCKED_BY_CLIENT`) and a real logged-in browser window may be sized too small to screenshot a
diagram usefully.
