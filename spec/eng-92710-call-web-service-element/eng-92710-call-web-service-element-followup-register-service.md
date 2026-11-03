# Jira draft: register a REST web service through the AI Toolkit

Created on 2026-11-03 as ENG-105475 "Register a REST web service through the AI Toolkit": project ENG, type Story,
component `bpms tools`, epic ENG-92704 "Create BP via AI Toolkit", link "1. Relates" to ENG-92710 "Call web service
element".

**Summary:** Register a REST web service through the AI Toolkit

## Description

### User story

As a user who gives an AI agent a business task such as "pull the orders from our ERP API into Creatio", I want the
agent to register the REST web service the task needs, so that a business process can call it with the Call web service
element without anyone setting the service up by hand in Studio.

### Context

- The Call web service element (ENG-92710 "Call web service element") calls only a web service that is already
  registered in Studio > Web services. The Process Designer offers the same choice: its service list is the registered
  service schemas (`VwWebServiceV2`), it has no "new service" entry, and the button next to the field only opens the
  selected service for editing (checked on a 10.1.37 stand, 2026-11-03).
- The run time loads the service schema by its UId and takes the base URI, the methods and the authentication from it,
  so an unregistered API cannot be called by that element at all.
- Today an agent has no way to register a service. With ENG-92710 alone, a task that names an unregistered API stops:
  the agent can only tell the user to register the service in Studio first.

### Scope

- Create a REST web service schema in a chosen package: code, caption, base URI, methods.
- Per method: code, caption, HTTP verb, URI path; request parameters (query, path, header, body) and response
  parameters, with data types, required flags, arrays and nested objects as Studio allows them.
- Read an existing service back (methods and parameter trees) without any secret.
- Modify a service: add a method, add or change a parameter; renames and deletes only with a warning (see the risks).

### Security

- The agent never enters a credential. Authentication settings (login, password, token, API key) are set by the user;
  the agent may reference an existing system setting, never a literal value.
- Some services take an API key as an ordinary request parameter (the shipped `GeminiApiSearch` has `Xgoogapikey`).
  The agent fills such a parameter from a system setting or a process parameter, never a constant.
- No read path may return secrets. The designer's service read returns the service's authentication settings; the
  toolkit never calls it and never echoes it, and every read output it adds is stripped of authentication settings.

### Risks to cover

- Renaming or deleting a method, or changing its parameters, breaks every process that calls it, silently. Measured on
  a 10.1.37 stand (2026-11-03): an element whose stored method name differs from the service's method only in letter
  case saves without an error; the next load of the process removes all of that element's service parameters; the run
  fails with `ItemNotFoundException: Collection item with name ... not found`. A change to an existing method must
  name the processes that call it before it is applied.
- Package dependencies: the Process Designer adds a dependency from the process package to the service package on save;
  the AI Toolkit process builder does not. A service registered in a package that the process package does not depend
  on runs on the same environment but breaks when the process package is installed elsewhere.

### Out of scope

- The Call web service element itself (ENG-92710 "Call web service element").
- SOAP services.
- Creating or storing credentials.

### Acceptance criteria

1. The agent can register a REST web service with its methods and parameters in a chosen package; the service then
   appears in the Process Designer's service list and in Studio > Web services exactly as if created there.
2. A process built with the Call web service element on that service runs and returns the response.
3. Reading a service returns its methods and parameter trees and never a password, token or key value.
4. A change to an existing method that processes call is refused or warned about, naming those processes.
5. No credential value is written or returned by any operation.

### Open questions

- Where the operation lives: a clio MCP tool over a platform endpoint, a CrtProcessBuilder package operation, or a
  ClioGate endpoint (the service schema manager may need elevated rights).
- Whether the read part doubles as the service discovery that ENG-92710 plans not to build in v1.
- Whether the user's credential entry can be guided (which system setting to create) without the agent touching it.
