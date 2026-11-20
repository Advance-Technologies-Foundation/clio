---
description: SensitiveErrorTextRedactor has two entry points - Redact for any text (envelopes, logs, CLI, MCP errors) and RedactSiteText for text a Creatio site wrote; the site-only rules (sign-in and database phrases, server instances, relative paths, key runs, the stack-trace cut) must never move into Redact; the paths wired so far are named here, the readers not yet wired are listed by file and member in SiteTextWiringTests' known-unwired list (A2, follow-up), a new reader must call RedactSiteText, and both entry points fail closed as one unit on a regex timeout
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
  - clio.tests/Common/SensitiveErrorTextRedactorSiteTextTests.cs
date: 2026-11-20
---

**What is true** - the redactor has two entry points. `Redact` runs over ANY text: clio's own CLI lines and
MCP errors, logs, and whole serialized JSON envelopes (`ClioRunTool.RedactSerializedText`). Its rules are only
the ones that are safe there: URIs, JWTs, scheme credentials after Basic/NTLM/Negotiate/Bearer and a Digest
client list, e-mails, dotted and single-label `host:port`, IPv6, private-range IPv4, Windows, `C:/` and POSIX
paths, and credential pairs. `RedactSiteText` runs `Redact` plus the rules that are only safe on text a Creatio
SITE wrote: the cut before a stack frame or an HTML page, the sign-in name in a database's own phrase
(`Login failed for user '…'`, PostgreSQL's role refusals), `database "…"` and `open server '…'`, the
connection-string sign-in and address keys and case-sensitive `…Uid` keys, a Windows server instance
(`SQL01\SQLEXPRESS`), a relative path (`./`, `../`, `~/`, and the backslash forms `.\`, `..\` decoded or
JSON-escaped as `.\\`, `..\\`), a drive-relative path (`C:foo\bar.config`, `C:Web.config`), and a key-like run
right after a secret noun.

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

**Backslash and drive-relative paths** - a Windows site names a file by a backslash relative path
(`See ..\..\Terrasoft.Configuration\Web.config`, found live through odata-create on 20 Nov), and odata-update and
odata-delete carry it JSON-escaped when the OData error has no string `message` and its raw JSON is the text
(`..\\..\\Terrasoft.Configuration\\Web.config`). The forward-slash rule never took either, and Redact's UNC arm took
the escaped tail and left `..\\..[redacted-path]`. The site-only rules now take:
- a backslash relative path, the WHOLE path with its leading dots. A separator is a run of one, two or four
  backslashes - decoded, JSON-escaped or escaped twice - so a path that mixes them goes whole (`.\\conf/x\y.config`).
  A `/` inside a segment is part of it (`.\conf/x.config`), and so is a placeholder Redact already wrote there
  (`..\\..[redacted-path]`, `..\[redacted-uri]\x`);
- a drive-relative path: a drive and segments behind a separator (`C:foo\bar.config`), a drive glued to a placeholder
  (`C:foo[redacted-path]`), or an UPPER-CASE drive and a file name with a lower-case extension (`C:Web.config`);
- the forward-slash relative path in an escaped text: it starts after `\u0022`, and its tail never takes the
  backslash of `\"`, nor of a `\uXXXX` that closes the token, so the closing escape stays whole.

A `\uXXXX` closes the token only before whitespace, a quote, `<`, `>`, `|`, a closing bracket or the end - where the
forward-slash tail always ended. Glued to more text, the escape stays in the path: Redact's pair, URI and instance
rules cannot start right after an escape, so stopping there showed `\u0022Password=hunter2`, `\u0022CORP\\jdoe` and
`\u0022;(SQL01\\SQLEXPRESS` that the path used to take with it (the differential runs of 20 Nov); master's own chain left `[redacted-path]]=hunter2`
of `../x/y.config\u0022Password=hunter2`, which now goes whole. The path rules run
before the server-instance rule, which read a path's later segments as `SERVER\instance` and kept the host in
`..\db01.internal:1433\x`, and once more after it, for a path right after the placeholder it wrote
(`SQL01\SQLEXPRESS../x/y.config`). A path starts at a token start: not after a word character, a dot, `/`, `~`, `-`,
a backslash or a CLOSING bracket (`"^[A-Z].\d{3}$"` is a pattern), but right after a complete `\uXXXX`, `\n`, `\r`
or `\t` escape or a placeholder. A separator never starts a written escape: `\n`, `\r` or `\t` before anything but a
lower-case letter, or a `\uXXXX` that closes the token; `\"` and `\\` never open a segment. So `failed.\nNext step`,
`done.\"`, `end.\\n`, `run \"CanManageSolution\"`, `Saved "Order".\nNext record`, `Loading...\nNext`, `8.1.0.5`,
`Version=10.0.0.0`, `Step C: retry`, `10:30`, `Range A:B`, `x:Item.Value` and `p:Item.name` stay. The first segment
after the dots may start like an escape (`\n`, `\r`, `\t` and a lower-case letter) only with proof - another
separator after it (`..\temp\Web.config`) or a lower-case file extension (`.\nlog.config`) - so
`Cannot delete "Order".\nthe record is referenced` stays; a later segment needs no proof (`..\x\nightly.bak`).
Every segment is atomic and its units start on different characters, so the rules read 100,000 characters of dots
and backslashes in linear time.
- KNOWN GAP: a single folder named like an escape, with no separator or extension after it (`..\reports`,
  `.\temp`), stays.
- KNOWN TRADE: an upper-case letter, a colon and a file name (`A:b.txt`) reads as a drive-relative path; in a
  JSON-escaped text a `\n`, `\r` or `\t` and a lower-case word right after a path read as one more folder, and a
  `\uXXXX` glued to text after a path goes with it.
- REDACT, UNCHANGED: on the escaped spelling the UNC arm still decides where a path with a host-like first segment
  ends, and its tail also takes a `,`, a `;`, a `\uXXXX` and a written or JSON `\n` with the word after it
  (`..\\..\\x\\Web.config, then` loses the comma; `\u0027..\\x\\y.config\u0027` loses the closing escape).
- FOUND, NOT FIXED HERE: Redact's pair rule and URI rule do not start right after a `\uXXXX` or `\n` escape
  (`\u0022Password=hunter2` and `\nhttps://host/x` pass Redact whole), and the server-instance rule reads neither the
  escaped spelling `SQL01\\SQLEXPRESS` nor stops at an escape (`Contact\u0027s` loses `\u0027s`).

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
"not text" (a field-name set, a marker, or a member of clio's own result type that shares a site-error name), or
KNOWN-UNWIRED with the change that wires it - "A2" for the 15 write-tool paths and the dynamic-case files (the
stacked PR A2) and "follow-up" for the remaining envelope readers, whose text passes `Redact` only until then
(odata-read's `TryClassify` debug detail is the follow-up entry in `KnownCallSites`). The scan sees exactly these
forms:
- the JSON names `errorInfo`, `responseStatus`, `ExceptionMessage`, `errorMessage`, `innererror` and
  `internalexception` written as a literal, on a line with no `[JsonPropertyName]`, `[JsonProperty]` or
  `[DataMember]` decoration;
- a read (a member access that is no call) of a member named `ErrorInfo`, `ResponseStatus` or `ExceptionMessage`,
  or of ANY member a clio source decorates with one of those JSON names, whatever its C# name. A decoration is
  `[JsonPropertyName("…")]`, `[JsonProperty("…", …)]` or `[DataMember(…Name = "…"…)]`, alone or in a list with other
  attributes. Today that makes every `.ErrorMessage` a read;
- one of those member names, alone or as the head of an extended pattern (`ErrorInfo.Message:`), right after a
  `{` or a `,` whose innermost open bracket is a `{` and followed by `:` - a property pattern, nested or in a
  switch arm (`is { Body: { ErrorInfo: { } } }`, `switch { { ResponseStatus: null } => … }`);
- a reference to a constant, a read-only field, a static expression-bodied property or an initialized
  auto-property that holds one of those names (`InnerErrorMemberNames`, `Names => ["innererror"]`);
- `ProcessDesignServerFault.ServerSaid` and `ServerReportedFailureText.Describe`.

A new read of one of those forms in an unlisted member, or one more in a listed member, fails the build. The scan
is by NAME, not by type, and does not see:
- a site's text read through a member of any other name that no clio source decorates with a site-error JSON
  name (an external library's DTO);
- a member name used as a pattern name in any other place than right after `{` or `,`;
- a property name built at run time or read from a parameter;
- a JSON path such as `"errorInfo.message"`;
- a name constant held in a local variable;
- reflection and `dynamic`.

Every "not text" entry is checked by `SourceGuard_ShouldProveEveryNotTextEntry`, in one of two forms:
- `name constant: …` - the member holds only a site-error name inside a constant's declaration, or references
  to one;
- `own <Type>: …` - the member reads only `.ErrorMessage` of the named types, each declared in clio with no
  site-error decoration, or FluentValidation's `ValidationFailure`.

`SourceGuard_ListSizes_ShouldBeTheStatedOnes` holds the list sizes:

| Wiring | Members | Reads |
|---|---|---|
| wired | 8 | 21 |
| not text | 23 | 30 |
| A2 | 30 | 46 |
| follow-up | 88 | 108 |

`KnownCallSites` holds 28 call sites: 18 central, 6 at a call site, 3 discarded and 1 follow-up.

**Fail closed** - `RedactSiteText` runs the cut, the whole Redact chain and every site rule inside ONE
`ExecuteRegex` guard, so a `RegexMatchTimeoutException` in any of them returns `[redacted]` for the whole text:
never the input, never a half-redacted text. Do not give a site rule its own guard or a fallback to the
unredacted input. `InjectRuleForTests` is the internal seam that proves it, and a real million-character
dotted run proves it without the seam.

**What must never go** - the site's reason: a database's own refusal sentence, column, table, constraint and
index names, CRM role words (`Access denied for role "Sales managers"`, `locked by user "Supervisor"`), the
words around a scheme (`The bearer token expired`, `WWW-Authenticate: Bearer realm=…`, `NTLM v2`, `basic
OAuth`, `negotiate TLS1.2`, `basic 8.1.0.5`), a version after a version word (`Version=10.0.0.0`), a count line
(`Failed:22, Passed:421, Total:443`), and `file.js:42:13`. `clio.tests/Common/Fixtures/site-text-redaction-cases.json` pins
350 invented inputs from the Accelerator host lane:
- A 46 and B 167 rows name the secret core that must go;
- C 111 rows must stay;
- D 25 rows are what the host removes and clio keeps (its documented trades, and the 12 rows clio declines by
  choice);
- E 1 row is what the host keeps and clio removes because master did: case-357, `bearer ABCD`, under the Bearer
  floor.

Every mustStay is the phrase that carries the reason. Its rule-9 SQL rows wait for PR B,
and its header and safety test forbid any real host, login, password, key, token or public IPv4 address.

**The Bearer floor** - master's rule was `\bBearer\s+[^\s,;"']+` with IgnoreCase, and it removed the whole run
after the word. `AllEntryPoints_ShouldLetNothingMastersBearerRuleRemovedSurvive_ExceptTheNamedTrades` applies that
exact rule to every fixture input, a corpus of token shapes and master's own Bearer cases. For each run the rule
removes, the test takes:
- every piece of two or more letters or digits (JSON escapes and HTML entities taken out first);
- the whole run without its trailing punctuation.

It drops each one that also appears anywhere else in the input. It then asserts that none of the rest appears in
the output of `Redact`, `RedactCredentials`, `RedactSiteText`, `RedactUntrustedOrNull` or
`RedactUntrustedSiteTextOrNull` once clio's placeholders are taken out. A one-character piece, and a piece that
also stands elsewhere in the input, is not checked. The only runs exempt are three named, documented trades:
- the word `token` or `tokens`, alone or with a prose ending such as `-based` ("The bearer token expired",
  "Invalid Bearer token.", "Bearer token-based access denied");
- a challenge parameter (`realm=`, `scope=`, `error=`, `error_description=`, `error_uri=`, `charset=`:
  "WWW-Authenticate: Bearer realm=…");
- clio's own placeholder.

So after Bearer nothing else stays, in any position: not a prose word, an acronym, a version or `2FA`. clio's own
sentences say "token-based (Bearer) authentication" and "a bearer token" instead of putting another word after
"bearer"; `OwnText_ShouldComeOutUnchanged_FromRedactAndRedactCredentials` holds them to it. That test checks every
string literal in `clio/` with a scheme word, a written escape before a letter, a bracket in a path-like token or the
start of a backslash or drive-relative path (`..\{0}\{0}.csproj` in clio's project template, which only a site rule
may take). It
skips only literals that ARE regular expressions: an argument of a `Regex` call, of `[GeneratedRegex]` or of a helper
whose name ends in `Regex`, and the constants those name. One expected change is named: an example Windows path in a
tool contract. The run is master's
with three changes, and none of them lets a credential character through:
- a backslash continues it except where it starts an escaped quote;
- `&amp;` is part of it;
- the quote around it may be `"`, `'`, `\"`, `\u0022`, `\u0027` or `&quot;` and stays.

**After Basic, NTLM and Negotiate** - master had no rule for these, so the line is drawn by position:
- In a HEADER POSITION, only a challenge parameter, one word of a short closed list (`token`, `realm`,
  `challenge`, `negotiate`, `negotiation`, `required`, `missing`, `invalid`, `expired`, `rejected`, `refused`,
  `failed`, `none`, `null`) or a placeholder stays. A header position is exactly one of:
  - a header name - `Authorization`, `Proxy-Authorization`, `WWW-Authenticate` or `Proxy-Authenticate` -
    optionally quoted, then `:` or `=`;
  - a key of the pair rule's credential list (`CredentialSecretKeys` and `credential(s)`: the pass words, `secret`,
    the token family, `api_key`, `client_secret`, `client_id`, `private_key`, `access_key`, `connection string`,
    `authorization`, `auth`, `bearer`, the cookie and session names), written QUOTED before its `:` or `=` (a JSON
    or dict key), or bare before an `=` (a pair);
  - a `key` or `auth` word JOINED by `-` or `_` to the word before it - a header name or a compound key - before
    its `:` or `=` (`X-Auth:`, `X_Key:`, `Api-Key:`, `Proxy-Auth:`).
  A key word with a colon in a sentence is no header position. The PAIR rule decides what happens to it next:
  - after `key` or `auth` and a colon, a scheme word and a word the scheme rule keeps in a sentence is prose and
    stays ("Primary key: Basic info", "auth: Basic mode is off"). Master removed the scheme word there, which is no
    credential;
  - after any other credential key and a colon - the pass words, `secret`, `cookie`, the token family
    (`token`, `access_token`, …), `client_secret`, `api_key`, … - the value is the secret whatever it looks like,
    and it goes ("password: Basic secrettokenvalue", "token: Basic Summer", "password: Digest secret");
  - with `=` after any key it is a pair, and the value goes.

  THE TRADE, removing wins: such a key in a sentence loses the words after it ("The token: NTLM handshake failed"
  becomes "The token=[redacted] failed"), and so does a Bearer value after a credential key, even a documented trade
  word ("token: Bearer tokens" becomes "token=[redacted]"). A joined `key` or `auth` ("X-Auth:", "Proxy-Auth:")
  keeps nothing either. KNOWN KEEP: `auth:` followed by Basic and a lower-case run stays ("auth:
  Basic abcdefghijkl").
- ELSEWHERE, in a sentence, these stay:
  - a prose word (all lower case, or one leading capital then lower case, optionally "-"-joined);
  - an acronym, alone or with `/n`, `/n.n`, `/ACRONYM`, `vn` or a dotted version of up to four short parts;
  - a version of up to four short parts;
  - a factor word (`2FA`);
  - a CamelCase word with ONE inner capital and no digit (`OAuth`).

  Everything else goes: `UsrPage1`, `AdminApiKey`, `1700000000.123456789`, `v2.0.0.1-secret`, and base64 even when
  it has inner capitals (`dXNlcjpwYXNz`). KNOWN KEEP: a lower-case run after Basic, NTLM or Negotiate in a sentence
  stays ("Expected Basic secrettokenvalue here"); master had no floor there, and a header position takes it.

**Counts, folders, server names and clamps** -
- A count word (`total`, `passed`, `failed`, `skipped`, `tests`, `ok`, …) with a number is a count when another
  one is on the same line; a real or a written line break ends the line. Alone, `word:port` takes the general
  single-label rule, so `ok:6379` and `tests:5432` go and `tests:4000` stays. Master had no single-label rule and
  removed none of these. KNOWN TRADES:
  - a lone `Failed:22` reads as a host on port 22;
  - a host named like a count word on a line with a count summary reads as a count and stays ("Cannot reach
    tests:5432 or ok:6379; Tests run: Failed:22, Passed:421, Total:443"). A test pins this trade.
- After a closing bracket in a Windows path, a written `\r`, `\n` or `\t` before a lower-case letter is the next
  folder (`jdoe(old)\temp`, `data[1]\nightly`): when the two readings disagree, the one that removes the path
  wins. KNOWN TRADE: a bracket followed by a written `\n` and a lower-case word is taken as part of the path
  ("see [C:\x\y.dll]\tnext" loses "\tnext"). Before a capital, a backslash, whitespace or the end it is a line
  break, and `\uXXXX` is an escaped quote.
- In `RedactSiteText`, a written `\r`, `\n` or `\t` before a lower-case letter is `SERVER\account` only after a
  server name: capitals, digits, `-`, `_`, `.` and `$`, at least one letter (`SQL01`, `CORP`, `DB-PROD`, `SQL_PROD`,
  `CORP.LOCAL`, `SRV$`), and not a status word (`ERROR`, `FAILED`, `FAIL`, `OK`, `WARN`, `WARNING`, `INFO`,
  `DEBUG`, `TRUE`, `FALSE`, `NULL`, `NONE`). After a number, a status word or any other word it is a line break, so
  "Error 404\nnot found" keeps its reason. KNOWN TRADE: an all-capitals word that is not a status word - dotted
  capitals included - eats the word after the escape ("Error from HTTP\nfailed here" becomes "Error from [redacted]
  here", "Use NOTE.TXT\nthen run" becomes "Use [redacted] run"). The rule checks for the
  backslash and the escape first and only then looks back at the name, so a long run of capitals is read in linear
  time.
- Every clamp before redaction (`ClampAtBoundary`) cuts back to a word boundary, drops the partial run and marks
  the cut with " …". A cut inside an open quote goes back to before the quote, so "for user 'zq invented login'"
  keeps nothing of the name.
- The JSON rule leaves exactly one credentials key alone: clio's own `constant-credentials-masked` flag, in that
  letter case and only with the value `true` or `false`. A string, a number or `null` under that key goes.

Every endpoint placeholder is `[redacted-uri]`, the one master already wrote for host:port.

**What breaks if you ignore it** - a site rule moved into Redact corrupts clio-run envelopes and clio's own
messages; a new extractor that calls Redact instead leaks the database login, the server name or the stack
trace into a model transcript; a per-rule timeout fallback hands back the text the chain did not finish.
