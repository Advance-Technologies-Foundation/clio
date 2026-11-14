---
description: The platform sends a SOAP 1.2 action only as a SOAPAction header and writes no WS-Addressing headers, so WCF SOAP 1.2 and wsHttpBinding endpoints refuse every call
applies-to:
  - clio/Command/WebServiceDesigner/WsdlParser.cs
ticket: ENG-105475
date: 2026-11-14
---

**What is true** — for SOAP 1.2 the platform sends `Content-Type: application/soap+xml;charset=UTF-8` with no `action`
parameter, the action in a `SOAPAction` header (`ServiceClientRequest.GetHeaders`), and an empty envelope Header.
Measured on stand `Creatio` (.NET Framework) on 2026-11-14 against a WCF host (.NET Framework 4.8): `BasicHttpBinding`
answers; a SOAP 1.2 custom binding answers `ActionNotSupported`, Action ''; a SOAP 1.2 WS-Addressing binding and
`WSHttpBinding` with security mode None answer `DestinationUnreachable`, To ''. ASMX answers on SOAP 1.2. Measured
workarounds: a request header parameter with the path `Content-Type` and the value
`application/soap+xml;charset=utf-8;action="<soap action>"`, or `soap-header` parameters `wsa:Action` and `wsa:To`.

**Why it is this way** — SOAP 1.2 is built like SOAP 1.1 with another content type; WS-Addressing is not implemented.

**What breaks if you ignore it** — Studio's quick setup (and `studio-pick`) chooses the SOAP 1.2 port, so a WCF
service registered the default way fails every call with a SOAP fault. `read-wsdl` warns and, for a WSDL WCF wrote,
names the SOAP 1.1 port to register; for others (ASMX answers) only as the way out of a failed test call.
