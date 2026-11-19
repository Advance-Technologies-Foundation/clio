---
description: SensitiveErrorTextRedactor has two entry points - Redact for any text (envelopes, logs, CLI, MCP errors) and RedactSiteText for text a Creatio site wrote; the site-only rules (sign-in and database phrases, server instances, relative paths, key runs, the stack-trace cut) must never move into Redact; the paths wired so far (the write-tool paths included) are named here, the readers not yet wired are listed by file and member in SiteTextWiringTests' known-unwired list (follow-up), a new reader must call RedactSiteText, and both entry points fail closed as one unit on a regex timeout
applies-to:
  - clio/Common/SensitiveErrorTextRedactor.cs
  - clio/Common/SiteResponse.cs
  - clio/Common/UntrustedText.cs
  - clio/Common/CreatioResponseError.cs
  - clio/Common/DataServiceResponse.cs
  - clio/Common/DataWriteDiagnostic.cs
  - clio/Command/DataServiceBatchCommand.cs
  - clio/Command/PageSchemaMetadataHelper.cs
  - clio/Command/McpServer/Tools/ODataKeyedWrite.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/PageDesignerHierarchyClient.cs
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PageCreateOptions.cs
  - clio/Command/SchemaTemplateCatalog.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs
  - clio/Package/SelectQueryHelper.cs
  - clio/Package/ServiceResponseJsonGuard.cs
  - clio/Command/CompileConfigurationCommand.cs
  - clio/Package/PackageBuildResult.cs
  - clio/Package/PackageBuilder.cs
  - clio/Package/InstallLogAnalyzer.cs
  - clio/Package/BasePackageInstaller.cs
  - clio/Command/CreateBusinessProcessCommand.cs
  - clio/Command/ModifyBusinessProcessCommand.cs
  - clio/Command/ProcessModel/ProcessDesignServerFault.cs
  - clio/Command/CompileBusinessProcessCommand.cs
  - clio.tests/Common/SiteTextWiringTests.cs
  - clio.tests/Common/SiteTextWritePathWiringTests.cs
  - clio.tests/Common/Fixtures/site-text-redaction-cases.json
date: 2026-11-19
---

**What is true** - the redactor has two entry points. `Redact` runs over ANY text: clio's own CLI lines and
MCP errors, logs, and whole serialized JSON envelopes (`ClioRunTool.RedactSerializedText`). Its rules are only
the ones that are safe there: URIs, JWTs, scheme credentials after Basic/NTLM/Negotiate/Bearer and a Digest
client list, e-mails, dotted and single-label `host:port`, IPv6, private-range IPv4, Windows, `C:/` and POSIX
paths, and credential pairs. `RedactSiteText` runs `Redact` plus the rules that are only safe on text a Creatio
SITE wrote: the cut before a stack frame or an HTML page, the sign-in name in a database's own phrase
(`Login failed for user '…'`, PostgreSQL's role refusals), `database "…"` and `open server '…'`, the
connection-string sign-in and address keys and case-sensitive `…Uid` keys, a Windows server instance
(`SQL01\SQLEXPRESS`), a relative path (`./`, `../`, `~/`), and a key-like run right after a secret noun.

**Why the site rules stay out of Redact** - each one breaks something Redact also handles. A rule that runs
to the end of a line runs to the end of a serialized envelope, which has no real line break. A backslash-run
rule eats `\"` and `\u0022` escapes, so the caller loses the whole response, not one field. The relative-path
rule takes `./MyPkg/descriptor.json` out of clio's own error. The sign-in phrases take a login the operator
typed on purpose. An any-quad IPv4 rule takes `Creatio 8.1.0.5` and `cliogate 2.0.0.32` (the PR #1374
decision), which is why Redact takes only the private ranges and not after a version word.

**Escapes inside Redact's own path rule** - a site message that quotes its own JSON
(`permissions to run \"CanManageSolution\"`) reaches a clio-run envelope as an escaped backslash followed by an
escaped quote. The old UNC alternative started on that `\\` and ran to the next space, so the operation name -
the whole reason - became `[redacted-path]` (found live through clio-run update-page, 19 Nov). A UNC start now
needs a host token and a further backslash after it and may not follow another backslash; a drive and an
escaped backslash pair are taken whole; and a backslash right before a quote ends a path, so no path match
takes half of an escape. Every new Redact rule shares the same escape-aware start (`EscapeAwareStartPrefix`).

**Where RedactSiteText is called** - at the point site text is read, never later. Wired so far: `SiteResponse.Carry`
(message and code, after clamping the input to 8x the 2,000-character cap), the `CreatioResponseError.TryDetect`
callers (odata-create, `ODataKeyedWrite` for odata-update and odata-delete, field validation, trigger smoke,
email templates, branding), the non-JSON preview inside `DescribeNonJsonResponse`, `ParseSaveErrorMessage`,
`DataServiceResponse.ThrowIfUnsuccessful`, and compile-status's diagnostics and message tail. The fenced form
`RedactUntrustedSiteTextOrNull` (`UntrustedText.FencedSiteText`) serves batch item errors,
`DataWriteDiagnostic.message` and the refused delete's inner message. `SiteTextWiringTests` drives each path
with a marker only `RedactSiteText` removes. Its source guard reads clio's code with comments and strings masked
and on any line ending: every call of those extractors must be in `KnownCallSites`, and at a "call site" entry the
variable the extractor writes to may be read only inside a `RedactSiteText` argument.

**Not every reader is wired yet** - the same guard finds every DIRECT read of a site's error text (the
`errorInfo`, `responseStatus`, `ExceptionMessage`, `errorMessage` and `innererror` names, the `.ErrorInfo`,
`.ResponseStatus` and `.ExceptionMessage` members, `ProcessDesignServerFault.ServerSaid` and
`ServerReportedFailureText.Describe`) and holds each one, by file and member, in `KnownSiteErrorReads`: wired
through a checked extractor, "not text" when the name only classifies a failure, or KNOWN-UNWIRED "follow-up"
for the remaining envelope readers, whose text passes `Redact` only until then (odata-read's `TryClassify` debug
detail is the follow-up entry in `KnownCallSites`). The write-tool files are wired and may hold no follow-up entry.
A new reader, or one more read in a listed member, fails the build. The scan sees those shapes only; a site's text
read through a DTO member of another name (`.ErrorMessage` of the BuildProcess and package-build answers) is in
the write-tool files, each with a behaviour case.

**The write paths** - the same rule, at the point each write tool reads the site's text: the designer and
template refusals (`PageSchemaMetadataHelper.DescribeRefusal`, used by `PageDesignerHierarchyClient` and
`SchemaTemplateCatalog`: update-page, sync-pages, get-page, create-page), `TryGetSchema` and
`TryLoadTemplateLocalizableStrings`, the catalog's non-JSON preview, the entity-schema designer's
`EnsureSuccess`, `SelectQueryHelper.DescribeServerFailure` and `ServiceResponseJsonGuard`'s body preview, the
compile verdicts (compile-creatio's `ProceedResponse`, `ApplyEnvironmentVerdict` and history lines,
`PackageBuilder.Fail`), the installer's `InstallLogAnalyzer.DescribeFailure` and invalid-archive message,
`ProcessDesignServerFault.ReadServerText`, the create and modify process failures, and compile-business-process's
fenced lines (`UntrustedText.FencedSiteText`). These texts become log lines as well as errors, and on stdio the
returned log is deliberately not redacted again (`McpPassthroughRedaction` redacts only on passthrough), so the
redaction at the read is the only one a stdio caller gets; `SiteTextWritePathWiringTests` asserts the marker in
both the result and the log. `DescribeRefusal` reads only `errorInfo`'s code and message: the whole `errorInfo` used
to be copied, so its `stackTrace` reached update-page's `error`. A compile diagnostic is redacted where it is
rendered (`PackageBuildDiagnostic.WithSiteTextRedacted`), not in the parser, because compile-status already fences
the history diagnostics it renders. `ReadServerText` redacts
before its cap; the WCF prefix `IsUnreadableRequest` looks for is prose no rule takes. Out of this rule by decision:
the raw streamed installation log (`BasePackageInstaller.ListenForLogs`), a separate finding.

**Fail closed** - `RedactSiteText` runs the cut, the whole Redact chain and every site rule inside ONE
`ExecuteRegex` guard, so a `RegexMatchTimeoutException` in any of them returns `[redacted]` for the whole text:
never the input, never a half-redacted text. Do not give a site rule its own guard or a fallback to the
unredacted input. `InjectRuleForTests` is the internal seam that proves it, and a real million-character
dotted run proves it without the seam.

**What must never go** - the site's reason: a database's own refusal sentence, column, table, constraint and
index names, CRM role words (`Access denied for role "Sales managers"`, `locked by user "Supervisor"`), the
words around a scheme (`The bearer token expired`, `WWW-Authenticate: Bearer realm=…`, `NTLM v2`, `basic
OAuth`, `negotiate TLS1.2`, `basic 8.1.0.5`), a version after a version word (`Version=10.0.0.0`), a count line
(`Failed:22`, `Total:443`), and `file.js:42:13`. `clio.tests/Common/Fixtures/site-text-redaction-cases.json` pins
350 invented inputs from the Accelerator host lane: A 46 and B 167 rows name the secret core that must go, C 112
rows must stay, and D 25 rows are what the host removes and clio keeps (its documented trades, and the 12 rows
clio declines by choice); every mustStay is the phrase that carries the reason. Its rule-9 SQL rows wait for PR B,
and its header and safety test forbid any real host, login, password, key, token or public IPv4 address.

**What a scheme credential is** - the WHOLE run after Basic, NTLM, Negotiate or Bearer that the old
`\bBearer\s+[^\s,;"']+` rule removed, so nothing it removed survives (a test runs master's Bearer cases through
every entry point). The run takes a backslash only as a whole JSON escape unit and may be quoted with `"`, `'`,
`\"`, `\u0022` or `\u0027`; what stays after a scheme word is a CLOSED list of prose words (an open "any lower-case
word" guard kept `Bearer secrettokenvalue`), an acronym with or without a version, a version, a factor word or a
CamelCase identifier. Every endpoint placeholder is `[redacted-uri]`, the one master already wrote for host:port.

**What breaks if you ignore it** - a site rule moved into Redact corrupts clio-run envelopes and clio's own
messages; a new extractor that calls Redact instead leaks the database login, the server name or the stack
trace into a model transcript; a per-rule timeout fallback hands back the text the chain did not finish.
