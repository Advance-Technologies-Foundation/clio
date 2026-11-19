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
  - clio/Command/ModifyProcessAsNewVersionCommand.cs
  - clio/Command/SetActiveProcessVersionCommand.cs
  - clio/Command/ProcessModel/LayoutChangeRelay.cs
  - clio/Command/BuildCaseClient.cs
  - clio/Command/DynamicCaseService.cs
  - clio/Command/ProcessModel/ProcessWriteDigest.cs
  - clio/Command/McpServer/Tools/CompileStatusTool.cs
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

**Not every reader is wired yet** - the same guard scans clio's code for DIRECT reads of a site's error text and
holds each reading member, by file and member, in `KnownSiteErrorReads`: wired through a checked extractor,
"not text" (a field-name set, a marker, a failure classifier, or a member of clio's own result type that shares a
site-error name), or KNOWN-UNWIRED "follow-up" for the remaining envelope readers, whose text passes `Redact` only
until then (odata-read's `TryClassify` debug detail is the follow-up entry in `KnownCallSites`). The write-tool and
dynamic-case files are wired and may hold no follow-up entry. The scan sees:
- the JSON names `errorInfo`, `responseStatus`, `ExceptionMessage`, `errorMessage`, `innererror` and
  `internalexception` written as a literal outside a `[JsonPropertyName]`/`[JsonProperty]`/`[DataMember]` line;
- a read of a member named `ErrorInfo`, `ResponseStatus` or `ExceptionMessage`, or of ANY member a source
  decorates with one of those JSON names, whatever its C# name - today that makes every `.ErrorMessage` a read;
- one of those member names in a property pattern (`is { ErrorInfo: { } }`);
- a reference to a constant or read-only field that holds one of those names (`InnerErrorMemberNames`);
- `ProcessDesignServerFault.ServerSaid` and `ServerReportedFailureText.Describe`.

A new read of one of those shapes in an unlisted member, or one more in a listed member, fails the build. The scan
is by NAME, not by type, and does not see: a site's text read through a member of any other name that no clio
source decorates with a site-error JSON name (an external library's DTO); a property name built at run time or
read from a parameter; a JSON path such as `"errorInfo.message"`; or reflection and `dynamic`.

**The write paths** - the same rule, at the point each write tool reads the site's text: the designer and
template refusals (`PageSchemaMetadataHelper.DescribeRefusal`, used by `PageDesignerHierarchyClient` and
`SchemaTemplateCatalog`: update-page, sync-pages, get-page, create-page), `TryGetSchema` and
`TryLoadTemplateLocalizableStrings`, the catalog's non-JSON preview, the entity-schema designer's
`EnsureSuccess`, `SelectQueryHelper.DescribeServerFailure` and `ServiceResponseJsonGuard`'s body preview, the
compile verdicts (compile-creatio's `ProceedResponse`, `ApplyEnvironmentVerdict` and history lines,
`PackageBuilder.Fail`), the installer's `InstallLogAnalyzer.DescribeFailure` and invalid-archive message,
`ProcessDesignServerFault.ReadServerText`, the four process write failures - create, modify, as a new version,
activate a version - through `ProcessDesignServerFault.SiteMessageOr`, `SiteWarnings` and `SiteText` (the message,
each warning on its own so a frame in one cannot cut the owed-compile warning after it, and the version and layout
element names), compile-business-process's fenced lines (`UntrustedText.FencedSiteText`), and the dynamic-case
tools (`DynamicCaseService` redacts an answer's code and message where they enter the result; `BuildCaseClient`
redacts a quoted body before its cap). These texts become log lines as well as errors, and on stdio the
returned log is deliberately not redacted again (`McpPassthroughRedaction` redacts only on passthrough), so the
redaction at the read is the only one a stdio caller gets; `SiteTextWritePathWiringTests` asserts the marker in
both the result and the log. `DescribeRefusal` reads only `errorInfo`'s code and message: the whole `errorInfo` used
to be copied, so its `stackTrace` reached update-page's `error`. A compile diagnostic is redacted where it is
rendered (`PackageBuildDiagnostic.WithSiteTextRedacted`), not in the parser, because compile-status already fences
the history diagnostics it renders. A file the environment names - in a diagnostic, an environment verdict or an
installation log's compiler line - is cut to its NAME before the redaction (`PackageBuildDiagnostic.FileNameOnly`,
which compile-status uses too): the redactor takes a whole absolute path as `[redacted-path]` and a relative
`Terrasoft.Configuration\...` or `Autogenerated\Src\...` one as a server instance, so the line stopped naming the
file to fix. An installation log tail is redacted with its lines apart and flattened to ` | ` only after, or the cut
never sees the line break before a stack frame. A designer refusal that redacts to nothing (only a stack trace) is
no reason: the caller says its own sentence. A save refusal's `addonsErrors` item gives its code and message only,
never its `stackTrace`. `ReadServerText` redacts before its cap; the WCF prefix `IsUnreadableRequest` looks for is
prose no rule takes. The previews and quotes clamp their redaction input to 8x their cap with
`SensitiveErrorTextRedactor.ClampAtBoundary`, the clamp the fenced rendering and `SiteResponse` use, so a token the
clamp would cut in two is dropped rather than shown as a head the redactor cannot recognize. The process write
commands' success-path warnings are logged each through `RedactSiteText` too, and so are the schema and version
names a successful write returns.

**Follow-up text at a write tool's sink** - a write tool also quotes text that a reader still on the follow-up list
produced, and that text is redacted where it enters the write tool's result or log, because the reader itself stays
unwired: `DynamicCaseService` redacts the page step's refusal (which quotes the RelatedPage add-on read through the
record page resolver), the package dependency manager's failure, the page write's error, and the server's notes,
warnings, leftovers and case names item by item; the process writes redact a describe-business-process read-back
failure (`ProcessWriteDigest`, the access-rights and flow-label guards, the verification warning); compile-creatio and
compile-package redact a compilation-history baseline that could not be read (the ATF data provider). Not
redacted at a sink, on purpose: transport texts (`BuildCaseClient`'s route and transport failures, compile-business-process's
transport detail, compile-creatio's request error), which carry no answer of the site, and the dependency manager's
own "Validation warnings from server" log line, which the manager writes itself and which is follow-up with it. A
file the environment names is shown by its last segment only when that is a plain file name (an extension, no
separator, drive, scheme or key pair) that the redaction leaves whole; anything else - a path ending in a separator,
a share host, a URI, a secret's fragment - reads `[redacted-path]`. An installation log tail leaves out stack frames
before it is taken, so a log that ends in frames does not open its tail with one.

**What the guard proves and what it does not** - a `wired:` entry of a write-tool file is checked read by read: each
counted read in the member must be an argument of a call to the named extractor, or the member must be that
extractor. A read that reaches the extractor another way (a variable, a record field, a result redacted upstream) is
listed `via: <extractor>; proven by <test>`, and the guard checks only that the named end-to-end test exists - the
test, not the scan, is the proof. Entries outside the write-tool files are not checked read by read, and a
`not text:` entry is accepted as written; both are where a mislabel can still hide, which is why the dynamic-case
page refusal, once labelled `not text`, now has an end-to-end test over a fake HTTP answer. Out of this rule by decision: the raw streamed installation log (`BasePackageInstaller.ListenForLogs`,
and `ReportLocallyModifiedSchemas`' raw log lines), a separate finding.

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
rows must stay (case-357 keeps only "bearer": clio is stricter there, as the Bearer floor requires), and D 25 rows
are what the host removes and clio keeps (its documented trades, and the 12 rows clio declines by choice); every mustStay is the phrase that carries the reason. Its rule-9 SQL rows wait for PR B,
and its header and safety test forbid any real host, login, password, key, token or public IPv4 address.

**The Bearer floor** - master's rule was `\bBearer\s+[^\s,;"']+` with IgnoreCase, and it removed the whole run
after the word. `AllEntryPoints_ShouldLetNothingMastersBearerRuleRemovedSurvive_ExceptTheNamedTrades` applies
that exact rule to every fixture input, a corpus of token shapes and master's own Bearer cases. It asserts that
no piece of any run the rule removes - no two-character letter or digit piece, and not the whole run - survives
`Redact`, `RedactCredentials`, `RedactSiteText`, `RedactUntrustedOrNull` or `RedactUntrustedSiteTextOrNull`.
The only exceptions are three named, documented trades:
- the word `token` or `tokens`, alone or with a prose ending such as `-based` ("The bearer token expired",
  "Invalid Bearer token.", "Bearer token-based access denied");
- a challenge parameter (`realm=`, `scope=`, `error=`, `error_description=`, `error_uri=`, `charset=`:
  "WWW-Authenticate: Bearer realm=…");
- clio's own placeholder.

So after Bearer nothing else stays, in any position: not a prose word, an acronym, a version or `2FA`. The run is
master's with three changes, and none of them lets a credential character through:
- a backslash continues it except where it starts an escaped quote;
- `&amp;` is part of it;
- the quote around it may be `"`, `'`, `\"`, `"`, `'` or `&quot;` and stays.

**After Basic, NTLM and Negotiate** - master had no rule for these, so the line is drawn by position:
- In a HEADER POSITION, right after `Authorization:`, `Proxy-Authorization:`, `WWW-Authenticate:`,
  `Proxy-Authenticate:` or a credential key (`token:`, `access_token=`, `password=` …), only these stay: a
  challenge parameter, one word of a short closed list (`token`, `realm`, `challenge`, `negotiate`, `negotiation`,
  `required`, `missing`, `invalid`, `expired`, `rejected`, `refused`, `failed`, `none`, `null`) or a placeholder.
- ELSEWHERE, in a sentence, these stay:
  - a prose word (all lower case, or one leading capital then lower case, optionally "-"-joined);
  - an acronym, alone or with `/n`, `/n.n`, `/ACRONYM`, `vn` or a dotted version of up to four short parts;
  - a version of up to four short parts;
  - a factor word (`2FA`);
  - a CamelCase word with ONE inner capital and no digit (`OAuth`).

  Everything else goes: `UsrPage1`, `AdminApiKey`, `1700000000.123456789`, `v2.0.0.1-secret`, and base64 even when
  it is all letters (`dXNlcjpwYXNz`).

Every endpoint placeholder is `[redacted-uri]`, the one master already wrote for host:port.

**What breaks if you ignore it** - a site rule moved into Redact corrupts clio-run envelopes and clio's own
messages; a new extractor that calls Redact instead leaks the database login, the server name or the stack
trace into a model transcript; a per-rule timeout fallback hands back the text the chain did not finish.
