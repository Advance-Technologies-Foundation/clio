---
description: PageBodyAstLinter rejects a call in handlers, converters or validators unless the callee is definitely initialized in the factory scope chain or is a known JavaScript/Creatio global
applies-to:
  - clio/Command/McpServer/Tools/PageBodyAstLinter.cs
ticket: 1312
date: 2026-09-12
---

**What is true** — the undefined-call lint is limited to direct identifier callees inside the
`handlers`, `converters` and `validators` properties **of the object the AMD factory returns**; a
property of one of those names anywhere else in the tree is ordinary metadata and is not scanned.
Member calls such as `request.$context.set(...)` are not treated as helper references. A name
satisfies a call only when it is DEFINITELY INITIALIZED, and that is demanded only once resolution
crosses a function boundary: `if (false) { function helper(){} }`, `let helper;` with no assignment,
and an assignment placed after the factory's `return` all leave the binding `undefined` and are
reported, while the same shapes resolved inside the calling function itself are accepted because the
statements really do run first. Only `if (true)` / `if (false)` are folded — no other control flow.
The factory's scope chain ends at the runtime globals: a helper declared outside `define(...)` does
NOT satisfy a handler call. A body with no `define(...)` at all falls back to the same
return-anchored rule applied to every function.

**Why it is this way** — Creatio Page Designer can regenerate a body while preserving a handler entry
but dropping hand-written module-scope declarations, and a JavaScript parser accepts the result
because an unresolved identifier is a runtime lookup, not a syntax error. Name existence alone was
not enough: a preloaded declaration whose value never lands throws just as the missing one does.

**What breaks if you ignore it** — `validate-page` and the write-path validators can report a body as
valid and `sync-pages` can save it, while the first handler invocation throws `ReferenceError` or
`TypeError` and the page cannot open. Two known gaps that follow from the anchoring: a factory that
returns a VARIABLE (`return schema;`) rather than an object literal leaves the rule silent, and a
helper stored on an object and called through a member expression is out of scope by design. Do not
replace this with a text scanner or a full interpreter: the former misreads JavaScript grammar and
the latter would execute untrusted page code.
