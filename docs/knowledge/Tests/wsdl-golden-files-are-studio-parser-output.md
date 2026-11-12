---
description: the *.expected.json files under clio.tests/Examples/Wsdl are the output of Studio's own WSDL wizard parser run under jsdom, not hand-written; jsdom misreads a type extension, so extensions are asserted by hand
applies-to:
  - clio.tests/Examples/Wsdl/
  - clio.tests/Command/WebServiceDesigner/WsdlParserTests.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — each golden file is what the creatio-ui web-service-wizard parser (`SoapWebServiceMethodBuilder`
with the `SoapWebServiceBuilder.getService` and `WsdlMergeService` logic, transpiled with TypeScript and run under jsdom)
returns for the WSDL beside it, with the HTTP ports removed and each method code's random `_xxxxxxxx` suffix cut. The
WSDLs are the public DNE Online calculator and synthetic ones written for clio; the wizard's own fixtures are not public
and are not committed. jsdom does not find `complexType > complexContent > extension` when the `complexType` is the
element the query runs on, so the harness drops a complexContent extension's own elements where a browser (and the
wizard's own unit test) keeps them; the extension case is therefore asserted by hand, not by a golden file.

**Why it is this way** — parity with Studio is the acceptance criterion, and only the wizard's own code can say what
the wizard outputs; the wizard has no server-side parser to call.

**What breaks if you ignore it** — editing a golden file to make a test pass turns a parity check into a check of
clio against itself; regenerating one from a WSDL with a complexContent extension under jsdom pins the jsdom defect.
Regenerate by running the wizard's parser on the changed WSDL and diffing against clio's parser output, as described.
