# Prevent unsafe page-helper repair advice

Issue: #1697. Status: done.

As a page author, I need lint diagnostics to direct helpers into code that survives the
next Designer save, so following the tool's advice does not recreate the failure.

Acceptance:

- Extra factory statements warn, including unused functions, constants and setup code.
- Direct handler/converter/validator calls cannot use discarded factory declarations.
- Imported dependency arguments and callback-local helpers remain valid.
- Undefined-call advice and command help point to client modules and MCP guidance.
- Unit and stdio MCP coverage pass; rejected real saves preserve the persisted body.
- An actual disposable-runtime Designer save reproduces the loss and preserves the repair.
- Remove the issue-owned runtime after validation and retain evidence outside it.

See [decision](../adr/adr-page-designer-safety.md) and
[validation](../page-designer-safety/page-designer-safety-qa.md).
