---
description: the platform WSDL download (DownloadWsdlEndpoint.GetXmlSchemaFilesContent) keys each file by its schemaLocation as written and sends ASCII codes, so non-ASCII text arrives as '?' and a relative schemaLocation fails the whole download
applies-to:
  - clio/Command/WebServiceDesigner/WebServiceDesignerClient.cs
  - clio/Command/WebServiceDesigner/WsdlReader.cs
  - clio/Command/WebServiceDesigner/WsdlParser.cs
ticket: ENG-105475
date: 2026-11-12
---

**What is true** — `rest/DownloadWsdlEndpoint/GetXmlSchemaFilesContent` (ServiceDesigner package, CanManageSolution)
takes `string[] urls`, downloads each one with `HttpClient.GetAsync(new Uri(url))`, pushes every `import`/`include`
`schemaLocation` it finds onto the same stack (at most 100 files, 10 MB each) and returns
`{success, errorInfo{message, stackTrace}, files:[{Key, Value:int[]}]}`. `Key` is the URL given, then each
`schemaLocation` exactly as written in the importing file; `Value` is `Encoding.ASCII.GetBytes` of the text, so every
non-ASCII character is `?`. It does not follow a `wsdl:import location=`. Traced from the platform source; the
calculator download (one file) measured on stand `Creatio` on 2026-11-12, and `read-wsdl` URL mode passed there.

**Why it is this way** — the endpoint only feeds Studio's browser-side wizard, which looks files up by the
`schemaLocation` strings it finds and decodes the bytes as UTF-8; ASCII was enough for the WSDLs it was built for.

**What breaks if you ignore it** — reading the bytes with the encoding the XML declaration names (`utf-16`) fails the
parse; looking an import up by a resolved absolute URL misses the file; a WSDL with a relative `schemaLocation` makes
`new Uri(...)` throw on the server, so the whole download fails with "Error downloading schema" and nothing to read
(use `wsdl-files`); a Cyrillic caption or name silently becomes `???` (read-wsdl warns and points at `wsdl-files`).
