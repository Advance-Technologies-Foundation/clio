# Classic user-task parameter pages

Issue #1600, child of Story #1598. Create a native Classic parameter page for an
existing workspace user task through CLI and MCP. Preserve independent task
creation, toolbox registration and deployment workflows.

Require workspace, owning package, task UId, new page name and caption. Generate
the native parent, mapping controls for In/Variable parameters, page resources and
FK11 association. Optionally install each of the three SVG icon resources. Never
change execution code, parameter identities/directions or an existing page.

Acceptance includes offline CLI/MCP parity and real Creatio page loading, icons,
mapping changes persisted after save/reopen, and downstream output selection.
Use retained labs only; all instances remain in place.
