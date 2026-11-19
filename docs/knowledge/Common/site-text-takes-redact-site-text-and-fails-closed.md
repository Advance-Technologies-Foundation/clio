---
description: SensitiveErrorTextRedactor has two entry points - Redact for any text (envelopes, logs, CLI, MCP errors) and RedactSiteText for text a Creatio site wrote; the site-only rules (sign-in and database phrases, server instances, relative paths, key runs, the stack-trace cut) must never move into Redact, every site-text extractor must call RedactSiteText, and both fail closed as one unit on a regex timeout
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
  - clio.tests/Common/SiteTextWiringTests.cs
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

**Where RedactSiteText is called** - at the point site text is read, never later: `SiteResponse.Carry`
(message and code, after clamping the input to 8x the 2,000-character cap), the `CreatioResponseError.TryDetect`
callers (odata-create, `ODataKeyedWrite` for odata-update and odata-delete, field validation, trigger smoke,
email templates, branding), the non-JSON preview inside `DescribeNonJsonResponse`, `ParseSaveErrorMessage`,
`DataServiceResponse.ThrowIfUnsuccessful`, and compile-status's diagnostics and message tail. The fenced form
`RedactUntrustedSiteTextOrNull` (`UntrustedText.FencedSiteText`) serves batch item errors,
`DataWriteDiagnostic.message` and the refused delete's inner message. `SiteTextWiringTests` drives each path
with a marker only `RedactSiteText` removes, and its source guard greps clio for every call of those
extractors: a new call site fails the build until it has a wiring case and an entry in `KnownCallSites`.

**Fail closed** - `RedactSiteText` runs the cut, the whole Redact chain and every site rule inside ONE
`ExecuteRegex` guard, so a `RegexMatchTimeoutException` in any of them returns `[redacted]` for the whole text:
never the input, never a half-redacted text. Do not give a site rule its own guard or a fallback to the
unredacted input. `InjectRuleForTests` is the internal seam that proves it, and a real million-character
dotted run proves it without the seam.

**What must never go** - the site's reason: a database's own refusal sentence, column, table, constraint and
index names, CRM role words (`Access denied for role "Sales managers"`, `locked by user "Supervisor"`), the
words around a scheme (`The bearer token expired`, `WWW-Authenticate: Bearer realm=…`, `NTLM v2`, `basic
OAuth`), and `file.js:42:13`. `clio.tests/Common/Fixtures/site-text-redaction-cases.json` pins 350 invented
inputs from the Accelerator host lane (class C rows must stay, class D are the host's own over-redaction
trades clio does not copy); its rule-9 SQL rows wait for PR B, and its header and safety test forbid any real
host, login, password, key or token.

**What breaks if you ignore it** - a site rule moved into Redact corrupts clio-run envelopes and clio's own
messages; a new extractor that calls Redact instead leaks the database login, the server name or the stack
trace into a model transcript; a per-rule timeout fallback hands back the text the chain did not finish.
