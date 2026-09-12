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

**Where the returned schema is looked for** — three anchors, all decided before any call is
resolved: an object literal returned by a `return` that is a DIRECT statement of the function body,
an arrow whose expression body IS that object literal (`() => ({ handlers: [...] })`), and a
`return <name>;` whose `<name>` a direct `const`/`let`/`var` statement of the same block initialized
with an object literal. Anything else — a `return` nested inside an `if` or a `try`, a schema built
by a call, a schema assembled by assignments written separately from the declaration — is not
discovered, and the rule then reports nothing at all for that body.

**Known false positives, accepted for now** — the rule is fail-CLOSED against its catalog of runtime
globals, so a bare call to a name the catalog does not list is a blocking Error even when the page
runs: a host library reached without declaring it as an AMD dependency (`$`, `_`, `moment`), and a
sloppy-mode implicit global (`helper = fn;` with no `var`/`let`/`const`, which really does create a
global in a non-strict AMD body). Both are reported as "not declared in the enclosing scopes". The
fix on the authoring side is one line (declare the dependency, or the binding), so the catalog was
left as it is rather than widened on speculation; widen it when a real page hits this.

**Deliberate fail-open cases** — an assignment made from inside ANY nested function of the factory
counts as initialization, whether or not that function is ever called, because a factory that
assigns its helpers from an `init()` it calls before the `return` is ordinary page code. That walk
records names rather than resolving them, so a nested function that SHADOWS the name still marks the
outer binding. A `switch` shares one block scope across its cases, so a `let` initialized in one
case counts as initialized for the others. All three leave the rule silent on a page that may throw,
which is the safe direction for a finding that blocks the write.

**Cost profile** — the nested-function assignment scan runs once per function scope over that
function's whole subtree, so a body with deeply nested functions costs O(depth x nodes) and retains
one name set per live scope. Ordinary and even generated page bodies are flat enough for this not to
matter; a body built specifically out of hundreds of nested functions each assigning hundreds of
names is the shape that would not be. If such a body ever appears, bound the collected set the way
`MaxTrackedOmittedNames` bounds the omitted-name sample - and make saturation fail OPEN, because a
name that stops being tracked turns into a blocking Error on a page that works.

**What breaks if you ignore it** — `validate-page` and the write-path validators can report a body as
valid and `sync-pages` can save it, while the first handler invocation throws `ReferenceError` or
`TypeError` and the page cannot open. One known gap follows from the anchoring: a helper stored on an
object and called through a member expression is out of scope by design. Do not replace this with a
text scanner or a full interpreter: the former misreads JavaScript grammar and the latter would
execute untrusted page code. And do not move the definite-initialization decision back into the call
resolution walk: a verdict that depends on where the assigning helper sits relative to the `return`
is one no author can act on.
