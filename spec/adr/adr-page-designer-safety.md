# Page Designer-safe helper validation

Status: Accepted. Issue: #1697.

Creatio Interface Designer regenerates a Freedom UI web page's factory body on save.
In a disposable 10.0.0.858 runtime, the save removed a factory helper and constant while
preserving the handler that called the helper. Recommending that authors restore a helper
before `return` therefore repeats the defect.

Use the existing Acornima AST and bounded finding collector. Warn for every direct factory
statement other than `return { ... }`. Reject direct section calls that resolve to factory
declarations with a distinct diagnostic; they are declared now, but not Designer-safe.
Keep AMD parameters in a preserved scope and retain ordinary callback-local resolution.
Recommend a client-unit module in `SCHEMA_DEPS`/`SCHEMA_ARGS`.

Do not introduce a JavaScript interpreter, a text scanner, or a new validation service.
Member-call analysis and a complete audit of all marker preservation are outside this repair.
The CLI continues to have its documented validation boundary; these AST checks run through
MCP `validate-page`, `update-page`, and `sync-pages`. Broader published guide policy is #1698.
