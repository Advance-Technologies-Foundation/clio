---
description: OPTION B - every SensitiveErrorTextRedactor entry point runs master 1915d7543's frozen chain first (MasterChain, hash-pinned), then clio's Extra, Site and ExtraCredentials rules, which only replace spans through one edit function; trades are none, master's over-matches are declared; SensitiveErrorTextRedactor has two entry points - Redact for any text (envelopes, logs, CLI, MCP errors) and RedactSiteText for text a Creatio site wrote; the site-only rules (sign-in and database phrases, server instances, relative paths, key runs, the stack-trace cut) must never move into Redact; the paths wired so far are named here, the readers not yet wired are listed by file and member in SiteTextWiringTests' known-unwired list (A2, follow-up), a new reader must call RedactSiteText, and both entry points fail closed as one unit on a regex timeout
applies-to:
  - clio/Common/SensitiveErrorTextRedactor.cs
  - clio/Common/SensitiveErrorTextRedactor.MasterChain.cs
  - clio.tests/Common/MasterChainHashTests.cs
  - clio.tests/Common/RedactorOptionBTests.cs
  - clio.tests/Common/RedactionMasterDifferentialTests.cs
  - clio.tests/Common/RedactionDifferentialCorpus.cs
  - clio.tests/Common/Fixtures/redaction-master-baseline.txt.gz
  - clio.tests/Common/RedactionSinks.cs
  - clio.tests/Common/RedactorSinkCompositionTests.cs
  - clio.tests/Common/RedactorTimingTests.cs
  - clio/Command/RuntimeVerification/RecordDeletionService.cs
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

**Option B: master's chain first, then span edits (20 Nov)** - every entry point is master `1915d7543`'s redactor,
unchanged, followed by what clio adds, and the additions may only replace spans:
- `Redact(x) = Extra(MasterChain.Redact(x))`; `RedactSiteText(x) = Site(Redact(x))`;
  `RedactCredentials(x) = ExtraCredentials(MasterChain.RedactCredentials(x))`;
  `RedactJsonCredentialProperties(x) = ExtraJson(MasterChain.RedactJsonCredentialProperties(x))`. `RedactAll`, the fenced
  and the console renderings run `Redact` or `RedactSiteText`.
- `MasterChain` (`SensitiveErrorTextRedactor.MasterChain.cs`) holds master's patterns, options and calls verbatim, in
  members of its own. Its `Redact`, `RedactCredentials` and `RedactJsonCredentialProperties` run their step lists
  (`RedactSteps`, `CredentialsSteps`, `JsonSteps`: a rule and the placeholder or named evaluator it writes) and nothing
  else. `MasterChainHashTests` pins the patterns against hashes recorded from master's build, and the step lists -
  calls, order, placeholders and evaluators - against recorded step hashes. The OUTPUT PIN
  (`MasterChain_ShouldReproduceMastersOutput_ForEveryCorpusString`) checks the chain's output for each of the 13,389
  corpus strings against master's build's, so an evaluator's body cannot change either. Never edit a pattern or a call
  there: clio's rules are `Extra`, `Site` and `ExtraCredentials`, and they come back only where they remove more.
- Every rule after the chain goes through ONE edit function, `ApplyEdits`. A rule returns (span, placeholder) edits and
  writes nothing else. Inside a replaced span a placeholder, a fence word and a bracket stay; an edit that cuts a
  placeholder is widened to all of it; overlapping edits are joined into one span; a run next to a kept placeholder
  needs no new one. It is linear: the edits are sorted once and each widening is a binary search over the placeholders
  (the first version compared every edit with every placeholder and took 12.9 s on 1.2 MB). `RedactorOptionBTests`
  proves, over the whole corpus and on each stage's REAL input (Extra on master's chain, Site on Redact, ExtraCredentials
  on master's credentials chain), that every run of letters or digits a stage writes outside a placeholder is a piece of
  its input, and that its output is its input with spans replaced.
- The site cut runs in `Site`, AFTER the chain, so master's quoted-value rule sees a value that crosses a stack frame.
  The cut recognises a frame the chain already rewrote (`at [redacted-uri]`), and replaces the whole trace or page with
  ONE placeholder (a collapsing edit): a 110 KB page no longer becomes 460 KB of `[redacted]([redacted]){…}` at the
  sinks with no cap (the designer's save error, the DataService failure, email-template).
- MASTER'S ORDER AT EVERY SINK: each sink cuts where master cut and nowhere else before the chain, and runs the site-text
  redaction where master ran `Redact`. The fenced renderings cut their input at master's 2,400 before the chain and their
  output at 300; the non-JSON preview cuts the body at master's 500 before the chain (`RedactSiteText(Truncate(body))`);
  a refused delete's inner message is master's first line cut at 500, then the fenced rendering; `site-response` redacts
  the whole value and caps after, as master's `Cap(Redact(value))`. A cut master did not have may run only after the
  chain. Redacting first and cutting after let the text shrink and move a token from beyond master's cut into the
  window (a 400-character URL, then `user@foo-…` across 500).
- What a cut BEFORE the chain leaves open at the end of the text goes too, through rules that only replace: a quoted
  value after master's `key=[redacted]` (`UnclosedValueAfterPairPlaceholderRegex`), a JSON credential property
  (`UnclosedJsonCredentialValueRegex`), a JWT with one or two of its segments (`PartialJwtAtTheEndRegex`), and, site
  only, the sign-in or database name of a database phrase (`UnclosedSignInOrDatabasePhraseRegex`: `for user 'zq inv`).
- WORK, NOT THE CLOCK (B3): the redactor counts its own work per thread (`Work`: every character a rule or a step of
  master's chain scans, every match, edit, placeholder found or examined in the edit function's binary search, and
  piece kept). `RedactorTimingTests` proves linearity with it - the work of 200,000 characters is at most 2.2 times the
  work of 100,000 of the same shape, for every entry point and nine shapes (measured 1.85-2.07) - and the fence bound:
  both fenced renderings do exactly the same work for 1 MB as for 10 KB. One coarse clock check stays (1 MB of the
  slowest shape under 8 s, `[NonParallelizable]`).
- THE WORK BUDGET: an entry-point call fails closed past `WorkPerCharacter` (500) units per input character plus
  `WorkConstant` (200,000) - ten times the worst shape measured (48.5 units per character, pair-dense text through
  RedactSiteText). It counts rule passes x length, matches, edits, placeholders found, binary-search probes and pieces
  kept, and its outcome for those is deterministic; it cannot see the time a regex spends backtracking inside itself.
- THE PER-CALL DEADLINE (B4): every regex run - master's chain steps, clio's rules, every evaluator regex,
  `CountLines`, the count-word rule, the cut and the fence words - goes through `Bounded`, which gives it a match timeout
  of min(its scaled timeout, the time left before the call's 20 s clock backstop), stepped down to 62/125/250/500 ms or
  a doubling of 1 s; with under 62 ms left the call fails closed at once, and a budget check follows every evaluator.
  A run with several matches goes through `BoundedMatches` / `BoundedReplace`, which re-bound EACH match by the time left
  when it starts (B5): Regex.Matches and Regex.Replace restart one timeout per match, so the last match before the
  deadline got a whole step (five matches under a 1 s timeout ran 2,049 ms). `CredentialShapeMasker.Mask` runs its five
  rules (master's patterns) the same way.
  `BoundedMatches` refuses (throws at its first use) a right-to-left rule or a pattern with "\G", for which a fresh
  `Match(text, start)` would not give Regex.Matches' matches; none run for many matches has either - only
  `ListContinuationRegex` has "\G", and it is asked one `IsMatch(text, start)`. A rule with an infinite timeout runs as
  master's 1 s, scaled and under the deadline, never unbounded. A rebuilt rule is cached per rule instance and step, by
  reference (B6).
  The scaled timeout is the rule's own 1 s below 250,000 characters and 1 s per 250,000 characters above it (to 16 s),
  because a rule that never matches scans the whole text in one attempt (5 MB took up to 0.9 s here, idle). So a call
  ends within the backstop plus one small step. THIS CHANGES HOW MASTER'S CHAIN RUNS: its patterns, options, calls and
  outputs are master's wherever master completes, but its timeout is not master's fixed 1 s on a long input or near the
  deadline, so where master failed closed this build may complete. 5 MB of the nine shapes takes 0.4-4.0 s.
- THE REGEX AUDIT (B4): every clio-own regex was run against adversarial 2,000-character inputs. `DottedTypeNameRegex`
  (option B's own) backtracked exponentially (`Aa` + capitals + `.x`, 45 characters past any timeout); it keeps its
  pattern and runs on `RegexOptions.NonBacktracking`, the same language by construction. The cut
  (`StackOrPageBreakRegex`, 8 s on 20,000 characters of written breaks and spaces) tries only the first break of a run
  - its first look admits a break only (`(?=\r|\n|\\[rn])`; B4's `(?=[\r\n\\])` admitted every backslash of a written
  `\t`, 18 s on 80,000 characters, fixed in B5) -
  and `CredentialRunAfterPairPlaceholderRegex` (2.8 s on 20,000 spaces) looks at the next character before it looks
  back; both keep their first match / their matches and groups, proven by a differential (`RedactorRegexAuditTests`).
  `HeaderPositionRegex` is quadratic too but only ever sees a 64-character window. Master's chain is not changed; its
  host:port rule's quadratic run on `a.a.a.` is bounded by the deadline.
- A FAIL-CLOSED RESULT IS VISIBLE: a regex timeout or a call past its budget returns `[redacted-timeout]`, not the
  `[redacted]` a value gets (PlaceholderPieceRegex reads it, so later passes keep it), counts on `FailedClosed`, and is
  logged once through `DefaultFailedClosedLog` (the console logger's warning) with the entry point and the input
  length, never the text; tests capture it per thread through `InjectFailedClosedLogForTests`.
- Rules skip a text that lacks a literal every match of theirs contains (`ApplyRule(text, need, …)`, `ApplyRuleWhen`);
  an IgnoreCase rule's literal is compared ignoring case. Every prefiltered rule that runs is recorded (`Prefiltered`),
  and `EveryPrefilter_ShouldHoldWhereverItsRuleMatches` proves each condition exact over the corpus, master's chain
  output and Redact's output, as written and upper-cased. At 1.2 MB master's Redact takes 0.4-0.7 s and this one
  0.5-1.6 s (RedactSiteText up to 2.2 s, on JSON-escaped credentials).
- Each stage fails closed: an injected timeout before the chain, after it, after `Extra`, before or after `Site`, in the
  credentials or the JSON chain gives `[redacted]` (`EveryStage_ShouldFailClosed_WhenARuleTimesOut`).
- Rules that read master's own placeholders, so what master's chain left half done goes (`AfterMasterPlaceholders`):
  a credential-shaped run after master's `key=[redacted]` (master's pair rule took the scheme word,
  `Authorization: Basic X7q2` became `Authorization=[redacted] X7q2`), a quoted value after it, a Digest client list
  after a placeholder, and a host label before `host=[redacted]`.

**What #1990's heads hid and B brings back** - #1990's own chains (`5ae2a08f8`, `23f9d820f`) hid some secrets that
master's chain now reaches first and leaves half done, mostly because master's UNC rule takes a `;` or an escaped line
break into a path (`.\\SQLEXPRESS;Initial` and `\\nBearer` become `[redacted-path]`). `AfterMasterPlaceholders` and
`Extra` read those placeholders, and every rule only replaces:
- a connection-string part after a placeholder (`[redacted-path] Catalog=ZqDb`, `Source=`, `ID=`): the value goes to the
  next `;` or the end of the line, as in a `;` list, or one run where a quote stops that;
- a dotted token of 12 or more characters after `[redacted-path]`, to where master's Bearer value would end; a version
  and a dotted .NET name (`System.IO.FileNotFoundException`) stay;
- a server or domain name glued to `[redacted-path]` (`CORP[redacted-path]`, `\u0027SQL01[redacted-path]`);
- a drive, `[redacted-path]` and what master's POSIX rule left after the `:` it stops at (`D:[redacted-path]:6379`,
  `D:[redacted-path]:/Secrets/a`), as one path placeholder;
- `Bearer` glued to the word before it (`NextBearer abcdefgh.ijklmnop`), when the run is credential-shaped;
- a quoted value after `key=[redacted]` whose inner quote opens a name (`password=[redacted]"... for user "zq_user"`);
- a credential run after `key=[redacted]` that reaches a placeholder, unless it is the next pair (`token=[redacted]`);
- connection-string keys before that run (`Initial Catalog=[redacted] abcdefgh.ijklmnop`);
- a private address glued to letters (`retry10.0.3.17`) and a host:port glued to a word (`db01.internal:1433Range`),
  each with the glued letters, so a placeholder never splits one word into two;
- site only: an instance name after the placeholder that took its server (`[redacted]\SQLEXPRESS`), the sign-in
  phrase in `\u0022`, `\u0027` and `\"` quotes, a drive-relative path glued to `[redacted-path]` with dots or slashes
  (`C:foo.bar[redacted-path]`, upper-case drive only), a forward-slash drive-relative path (`C:foo/bar.config`), a
  forward-slash tail glued to `[redacted-path]`, and a first segment that starts like a written escape when it is glued
  to `[redacted-path]` (`.\tlnL.x[redacted-path]`).
Over 73,600 strings, B shows no secret-like token either head hid (0 at every entry point and both fenced renderings).
Path fragments still shown (22 strings against `5ae2a08f8`, 9 against `23f9d820f`, all at `RedactSiteText`): a
drive-relative path of ONE segment whose tail master's chain took (`C:I2Ni`, `C:WN)`; the rule needs a second segment or
an extension so `Range A:B` and `Step C: retry` stay), and paths glued to a word or a time (`x:Item.ValueC:BE5q`,
`10:30../GVM/x`, `UsrStatus./cLJ`), where no path rule may start.
What B still shows that a head hid is prose, key names and SQL statement text: the fixture keeps `VALUES (N'Jane Doe', ...)`
and `@Phone = N'+380...'` in `mustStay`, and the heads removed them only in the escaped spelling.
Over-matches these rules add: the connection part takes the rest of its line (`Catalog=ZqDb Login failed for user ...`
goes whole); a glued address takes its word (`retry10.0.3.17` becomes `[redacted-uri]`); the drive tail takes `:line`
in `D:/src/x.cs:line 42`.

**Trades: none, by construction.** `RedactionMasterDifferentialTests` runs the whole corpus - the fixture in three
spellings, the edge strings across every clamp and cut, 252 strings whose prefix (a URL, a credential pair, a
JSON-escaped password, a path) shrinks under redaction with a token across the 300, 500 or 2,400 cut, and 3 x 4,000
seeded strings: 13,389 - through every entry point and every sink of `RedactionSinks.All`, against
`Fixtures/redaction-master-baseline.txt.gz`. The baseline records master's REAL sink outputs, driven through master's
own seams: Redact, RedactCredentials, RedactJsonCredentialProperties, both fenced renderings, site-response's message
and code, the non-JSON preview, the OData error call sites, the keyed write, the DataService failure, the designer's
save error, the batch item error, compile-status' diagnostic and message tail, a refused delete's inner message and
DataWriteDiagnostic. No token master removed may show at any of them. A sink that cuts AFTER its chain (the fenced 300,
site-response's cap) is held to what master's output and master's chain over the same uncut input both removed: a
token only that cut dropped was cut, not redacted. The test asserts that all 13,389 strings were compared and that every
baseline line was seen, that no redaction inside a sink failed closed (a fail-closed output shows no token, so it would
pass vacuously), and that a sink throws exactly where master's did (the baseline records master's throws; there are
none). Branding's and email-template's OData error, which master passed on raw, are read through the caller's
`Redact`, as the DataService failure and the designer's save error are, so no sink is compared vacuously. Call sites inside an instance method that needs a client are read through their seam
(`TryDetect`) with the call site's expression, which the source guard pins; `RedactorSinkCompositionTests` drives
odata-create, odata-field validation and email-template through their real code with a substitute client.

**Declared over-matches** - what master's chain removes that #1990's own rules kept. The fixture lists 32 rows in
`overMatch` with `overMatchReason`, and asserts the fragments ABSENT:
- master's Bearer rule takes the scheme word with its credential: `The bearer token expired` becomes
  `The [redacted] expired`, likewise `Invalid Bearer token.`, `Bearer token-based access denied`,
  `WWW-Authenticate: Bearer realm=…`, and the kept-side `Authorization: Bearer` of the A rows;
- master's pair rule takes the scheme word as the value and writes `key=[redacted]`: `Authorization: Basic`,
  `password=Basic`, `token: Bearer`, `Authorization: Digest`, `auth: Basic mode is off` (`auth=[redacted] mode is off`),
  and the identifier after `token:` (`token: expired` becomes `token=[redacted]`);
- master's pair value runs across `&` (`client_secret=s3cr3t&api_key=abc` keeps no key name);
- master's host:port rule reads a file and its line as a host and a port (`UsrOrderPage.js:42:13` becomes
  `[redacted-uri]:13`);
- master's Windows path rule takes a closing bracket, an escape and what follows (`(C:\x\y.dll)` loses `)`;
  `end.\\n` becomes `end.[redacted-path]`), and reads an escaped backslash before an escaped quote as a UNC start:
  #1990's CanManageSolution fix does not hold. With the default encoder the operation name goes and the envelope
  parses; with the relaxed encoder the name stays and the envelope no longer parses, as on master;
- clio's own sentences with "bearer token" lose those two words wherever they pass `Redact` (the own-text corpus test
  declares them);
- R3, removing wins: a dot after a closing bracket starts a site path (`"^[A-Z].\d{3}$"` loses `.\d{3`).
`MastersChain_ShouldRemoveMore_AsDeclared` pins each changed test case with its reason.

**Residue against #1990's head** - a prose-shaped credential after a scheme word after a credential key shows:
`password: Basic secrettokenvalue` gives `password=[redacted] secrettokenvalue`. Master's pair rule took the scheme
word and left no trace of it, so the run cannot be told from the sentence after a removed value
(`Authorization: Bearer x returned 401` gives `Authorization=[redacted] returned 401`); `Extra` takes the run only when it
is credential-shaped (a digit, an inner capital, all capitals, `+ / = _`, or quoted). Master shows these too.
`Residue_ProseShapedRunAfterASchemeWordAfterACredentialKey_Shows` pins them.

The sections below describe the rules as Extra and Site rules; where they say a rule keeps a word that master's chain
removes, the chain decides (see the over-matches above).

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
(message and code, capped after, as master capped them), the `CreatioResponseError.TryDetect`
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
- No clamp of clio's own runs before redaction (option B, master's order). `ClampAtBoundary`, #1990's boundary clamp,
  is gone: master's own cuts are plain cuts, and what they leave open at the end of the text goes through the
  end-of-text rules above, so "for user 'zq invented login'" cut inside the name keeps nothing of it.
- The JSON rule leaves exactly one credentials key alone: clio's own `constant-credentials-masked` flag, in that
  letter case and only with the value `true` or `false`. A string, a number or `null` under that key goes.

Every endpoint placeholder is `[redacted-uri]`, the one master already wrote for host:port.

**What breaks if you ignore it** - a site rule moved into Redact corrupts clio-run envelopes and clio's own
messages; a new extractor that calls Redact instead leaks the database login, the server name or the stack
trace into a model transcript; a per-rule timeout fallback hands back the text the chain did not finish.
