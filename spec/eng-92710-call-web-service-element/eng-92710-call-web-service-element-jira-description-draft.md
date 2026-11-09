<!-- Written to ENG-92710 "Call web service element" description on 2026-11-03 after the owner's yes; read back the same day.  AC6 and AC8 rewritten at gate 1 on 2026-11-03 after the owner's yes; read back the same day. -->

**Goal:** Support the **Call web service** element: call a web service registered in Studio > Web services, pass a request, receive the response. The element is the core `ProcessSchemaWebService` (manager item `652b598d-cdd5-49d5-a86d-aafac88213f3`), not a user task; the legacy `WebServiceUserTask` is an unused SOAP stub and is not built.

**Status:** Not implemented. Plan agreed with the owner on 2026-11-03 (20 decisions).

**Scope:** select the registered service and method; request parameters, including a composite request body; map inputs; read the response into parameters, including a composite response read into a declared Collection (up to two levels). REST only.

**Not in scope:**

* Registering a web service (base URI, methods, authentication): ENG-105475 "Register a REST web service through the AI Toolkit". The Process Designer also works only with registered services.
* Mirroring a web-service output into a new parameter, scalar-array (ObjectList) parameters, three or more nesting levels and the designer's "Create from element" grouping: ENG-92730 "Complex / structured parameter types".
* SOAP services and multi-instance Call web service elements: refused with a message.

**Technical:** the builder generates the element's parameters the way the Process Designer does (nested items always) and writes the designer's mapping rows; server serialization is verified against designer-built captures; values are mapped through the existing mapping surface (Task 6); composite types beyond two levels are ENG-92730 (this replaces the earlier reference to Task 5).

**Deliverable:** a `webService` block `{service, method, timeoutSeconds?, ignoreErrors?}` in create / modify; server-side serialization verified against captures; `describe-business-process` read-back; tests, docs, guidance article and MCP surface.

**Estimate:** about 37 agent hours (range 22-65) and about 12 owner hours, about 6 working days from the agreed plan to the last merge. The earlier "~3 days" was the Confluence "AI Est." column (working days for one person, including review and QA), not agent coding time.

## Acceptance criteria

1. The `webService` configuration (service, method, optional timeout in whole seconds, optional ignore-errors flag) is accepted on create, `addElement` and `setElement`. Request parameters, including a composite request body and the raw request body, are set through the existing mappings on the element's own parameters, addressed by the names `describe-business-process` returns.
2. An unknown or ambiguous service or method fails with a message listing the available services (codes and captions), or the methods of the resolved service; a missing service is reported as needing registration in Studio > Web services. Names are matched case-insensitively and stored in their canonical spelling.
3. Inputs are mapped in through the existing mappings. The response is read into process parameters; a composite response is read through a declared Collection parameter (up to two levels) with item mappings.
4. The ignore-errors flag is stored on the element; Success, Http status code and Response body are available as element outputs.
5. Server serialization matches a designer-built Call web service capture, including the element's mapping rows.
6. Changing the service or method replaces the element's parameters as the designer does; Success, Http status code and Response body keep their identity, so steps that read them stay valid. The change lists the bindings it drops, with their values, and is refused while another element or a process parameter still reads a parameter that would be regenerated.
7. `describe-business-process` returns the service, method, timeout, ignore-errors flag and the element's parameter tree, including unbound inputs, and reports an unresolved service or method as an issue. A modify that lets the platform remove the parameters of such an element says so.
8. No data stored in the web service itself (parameter default values, authentication settings, credentials in the base URI) is returned in any builder output. The element's own bindings are returned the way describe returns them today; the guidance tells agents to take keys and tokens from a system setting or a process parameter, never a constant.
9. Tests, docs, the guidance article and the MCP surface are updated.

**Reference:** ENG-92730 "Complex / structured parameter types"; ENG-105475 "Register a REST web service through the AI Toolkit"; Task 6 (value mapping).

---

Source: Task 22 — [Task list: Add business process generation via AI instructions](https://creatio.atlassian.net/wiki/spaces/TER/pages/4758143001). Parent research: ENG-90883 "Add generation of business process with User task via AI instructions". Epic: ENG-92704 "Create BP via AI Toolkit".
