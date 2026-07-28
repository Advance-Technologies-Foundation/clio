# ADR: User Profile Language Detection for Entity Creation

**Status**: Proposed — **Revision 4** (Rev 2 addressed B-1 / M-1..M-6 / Mi-1..Mi-6; Rev 3 closes re-review NEW-1..NEW-6; Rev 4 adds the post-deploy script/culture write guard — Decision 5)
**Author**: Architect Agent
**PRD**: [prd-user-profile-language-detection.md](../prd/prd-user-profile-language-detection.md)
**Jira**: ENG-91044
**Created**: 2026-06-09
**Revised**: 2026-06-12
**stepsCompleted**: [1, 2, 3, 4]

> **Revision 2 note** — The first draft was reviewed adversarially and found to contain factual
> errors and correctness gaps. This revision keeps the sound overall shape (dedicated resolver +
> environment-bound factory + threaded culture argument + `en-US` fallback) but corrects every
> Blocker/Major and incorporates the Minors. The substantive changes:
> - **B-1**: `userCulture.displayValue` is now validated via `CultureInfo.GetCultureInfo` before
>   any use; unparseable/empty/missing → `CultureResolution.Failed("userCulture-missing")`.
> - **M-1**: creation paths never fall back to `CultureInfo.CurrentCulture`; the effective culture
>   is always non-null (override > resolved profile > `en-US` constant). Regression safety now
>   comes from `en-US`, not from `CurrentCulture`.
> - **M-2**: `NormalizeTitleLocalizations` does **not** currently take `cultureName` — a signature
>   change is now an explicit work item.
> - **M-3**: explicit in-scope / out-of-scope table for every caption-emitting verb.
> - **M-4**: `GetApplicationInfo` is no longer a hard precondition; an explicit `--caption-culture`
>   or a usable map entry skips resolution.
> - **M-5**: the per-environment cache is moved into a **singleton-held, environment-URI-keyed**
>   store so it survives across CLI processes' MCP host calls.
> - **M-6**: resolver contract is now `Task<CultureResolution> ResolveAsync(CancellationToken)`,
>   matching `PlatformVersionResolver`.
>
> **Revision 3 note** — second adversarial pass approved B-1/M-1/M-4/M-5/M-6 + all Minors and
> surfaced two Major hand-off gaps plus four Minors, now closed:
> - **NEW-1**: the `NormalizeTitleLocalizations` signature change has 8 call sites; the two missing
>   ones (`UpdateEntitySchemaCommand.cs:171`, `EntitySchemaTool.cs:78,462`) are now explicit In-scope
>   rows assigned to Story 4a/4b — they would otherwise silently keep `en-US` via the `= null` default.
> - **NEW-2**: `ApplicationInfoService.cs:496` `GetCurrentCultureName()` is triaged as an Out/read row
>   and the AC-08 grep file-list is pinned to exclude it.
> - **NEW-3**: cache concurrency semantics documented ("at most once per TTL sequential; duplicate
>   concurrent probe tolerated") so the test plan does not over-assert.
> - **NEW-4**: AC-06 parity test is now mapped to every In creator story (4a, 5, 6), not only 4a.
> - **NEW-5**: cache key is intentionally `Uri`-only; same-URI-different-user is out of scope.
> - **NEW-6**: `CultureResolution` consumers MUST branch on `Success` before reading `Culture`
>   (invariant documented on the record) so the hard-abort path cannot be bypassed.
>
> **Revision 4 note** — After the feature shipped (PR clio#682) the bug reproduced: an agent
> detected the profile culture correctly (`en-US`) but authored caption TEXT in another language
> (Cyrillic) and stored it under the `en-US` localization key, so the entity rendered foreign-language
> labels for an English profile. The resolver was never at fault — the gap was that the contract
> never required the caption *text* to be in the resolved/declared culture's language, only that the
> `en-US` key be present. Rev 4 closes this with **Decision 5**: a deterministic write-path
> script/culture guard plus sharpened detect-once guidance.

---

## Context

clio derives the caption/label culture for created Creatio entities from the *host machine's*
`CultureInfo.CurrentCulture` (`EntitySchemaDesignerSupport.GetCurrentCultureName()`, line 121) or
from hardcoded `"en-US"` literals scattered across creation paths. When the operator's machine
locale differs from the language set in the connected Creatio user's profile, captions are produced
in the wrong language. The PRD (ENG-91044) mandates a FULL behavior change: clio must resolve the
**connected environment user's profile culture** server-side and use it as the effective caption
culture for generated names/labels/captions, while keeping `en-US` as the universal fallback and
preserving the existing `en-US`-anchored localization-map contract.

The culture source is locked by the PRD: `ApplicationInfoService.svc/GetApplicationInfo`
(`ServiceUrlBuilder.KnownRoute.GetApplicationInfo` / `CreatioServicePaths.GetApplicationInfo`),
read via `IApplicationClient`, no cliogate. See **Decision 0** for the exact field and how it is
validated.

## Decision

Introduce a per-environment **`ICurrentUserCultureResolver`** service, built by an
`ICurrentUserCultureResolverFactory` (mirroring the existing `IPlatformVersionResolverFactory`
precedent), that reads `applicationInfo.sysValues.userCulture.displayValue` from
`GetApplicationInfo` via `IApplicationClient`, **validates the string with
`CultureInfo.GetCultureInfo`**, and returns a `CultureResolution` record. The resolved culture is
**threaded as an explicit `cultureName` argument** into the `EntitySchemaDesignerSupport` helpers
and into the creation paths, replacing `GetCurrentCultureName()` and hardcoded `en-US` literals in
the in-scope verbs (Decision 4 table). Creation paths always pass a **non-null effective culture**
(precedence: explicit `--caption-culture` > resolved profile > `en-US` constant); they never
read `CultureInfo.CurrentCulture`.

The resolver's contract is async (`Task<CultureResolution> ResolveAsync(CancellationToken)`) to
match the precedent and avoid blocking the long-lived MCP host loop. The per-environment cache
lives in a **singleton-held, environment-URI-keyed store** so it survives across MCP tool calls and
is the single source of "resolve once per session" (AC-05 / FR-10).

Resolution is **not** a hard precondition for creation: if the caller supplied `--caption-culture`
OR the supplied localization map already contains the needed key, creation does not depend on the
`GetApplicationInfo` round-trip (M-4). The hard CLI `Error:` + non-zero abort is reserved for the
case where there is no override and no usable map entry. On the MCP path a failure returns a
structured `cultureResolution` signal (`success:false`, `reason`) that the prompts/instructions/
resource guidance turn into an explicit ask-the-user behavior (FR-06).

## Decision 0 — Which field is authoritative, and how is it validated? (B-1)

**Verified against the creatio-ui frontend** (`/Users/a-kravchuk/Projects/creatio-ui`):

| Frontend usage | Field read | Treatment |
|---|---|---|
| `apryse-language.service.ts` L19 | `sysValues?.userCulture?.displayValue` | used directly as a culture code; `\|\| defaultLanguage` on miss |
| `ckeditor4-creatio/assets/config.js` L16 | `culture.displayValue.substr(0,2)` | used directly as a language code |
| `devkit/.../DEFERRED_REMOTE_MIGRATION_AI_GUIDE.md` L978-979 | `sysValues?.userCulture?.displayValue \|\| DEFAULT_CULTURE_NAME` | the canonical pattern |
| schema-designer `base-schema-designer-page.component.ts` L128/133 | `userInfo.cultureInfo?.sysCultureName` → `{ cultureName, value }` | builds the caption `cultureName` for new localizable strings |

**Conclusion.** `userCulture.displayValue` carries the **BCP-47 code** (`"en-US"`, `"fr-FR"`,
`"uk-UA"`) — it is the same code the frontend feeds directly to culture-aware APIs and is what the
schema designer puts into a localizable string's `cultureName`. (The sibling `cultureInfo.sysCultureName`
that the schema designer uses is **not** part of the `GetApplicationInfo` payload, so clio cannot
read it; `userCulture.displayValue` is the in-payload equivalent and is what FR-01 locks.) The
distinction in the PRD sample is deliberate and must be respected:

- **culture objects** (`primaryCulture`, `userCulture`): `displayValue` = the **code** (`"en-US"`),
  `value` = a GUID. → read `displayValue`.
- **language objects** (`primaryLanguage`): `displayValue` = a human label
  (`"English (United States)"`), `codeValue` = the code. → these are NOT used.

**Validation requirement.** The resolver MUST treat `displayValue` as untrusted and validate it
before use:

```csharp
// inside the resolver, after extracting the raw displayValue string:
if (string.IsNullOrWhiteSpace(raw)) {
    return CultureResolution.Failed("userCulture-missing");
}
try {
    CultureInfo culture = CultureInfo.GetCultureInfo(raw.Trim()); // throws CultureNotFoundException on garbage
    return CultureResolution.Resolved(culture.Name);              // normalised name, e.g. "en-US"
} catch (CultureNotFoundException) {
    return CultureResolution.Failed("userCulture-invalid");
}
```

An unvalidated `displayValue` is **never** written as a caption culture key. Empty/missing/
malformed all map to `CultureResolution.Failed(...)` (B-1, Mi-1). A dedicated unit test covers
the malformed-`displayValue` → `Failed("userCulture-invalid")` case and the missing-field →
`Failed("userCulture-missing")` case.

**Mi-1** — `userCulture` absent but `primaryCulture` present → `Failed("userCulture-missing")`.
clio MUST NOT silently substitute `primaryCulture` (that is the *system* culture, not the logged-in
user's profile language; FR-06 forbids a silent wrong-language default).

## Alternatives Considered

### Decision 1 — Where does culture resolution live?

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Extend existing `Clio.Command.ApplicationInfoService` | Reuses a class that already hits Creatio | That class is a *different* concept (installed-app metadata), not the `GetApplicationInfo` endpoint; conflating them is misleading | Rejected: name/semantics collision |
| B: New `ICurrentUserCultureResolver` + `…Factory` (PlatformVersionResolver pattern) | Single-responsibility; environment-bound via `IApplicationClientFactory.CreateEnvironmentClient`; testable with NSubstitute; cache lives in a separate singleton store (Decision 3) | One more small service + one cache store | **Chosen** |
| C: Make `GetCurrentCultureName()` non-static + inject client into `EntitySchemaDesignerSupport` | Smallest call-site change | `EntitySchemaDesignerSupport` is a stateless static helper used everywhere; turning it stateful/DI breaks dozens of call sites and the CLIO001/DI policy | Rejected: large blast radius |

### Decision 2 — How does the resolved culture reach the creators? (FR-03/FR-04)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Thread the effective culture as an explicit `cultureName` argument into the helpers | Explicit, testable; keeps `EntitySchemaDesignerSupport` static & stateless | Some helpers already accept optional `cultureName`; **`NormalizeTitleLocalizations` does NOT and must be changed** (M-2, below) | **Chosen** |
| B: Ambient `AsyncLocal`/thread culture override | No signature churn | Hidden state, hard to test, race-prone in the long-lived MCP host | Rejected |
| C: Set `CultureInfo.CurrentCulture` from the resolved value | `GetCurrentCultureName()` keeps working | Mutates process-global state in a shared MCP process; affects unrelated formatting; not thread-safe | Rejected |

**M-2 correction.** The helpers that ALREADY accept an optional `cultureName` are
`CreateLocalizableString` (L133), `GetLocalizableValue` (L245), `SetLocalizableValue` (L263), and
`GetRequiredLocalizationValue` (L231, defaults to `DefaultCultureName`). **`NormalizeTitleLocalizations`
does NOT** — its current signature is `(IReadOnlyDictionary<string,string>? values, string? fallbackValue, string fieldName)`
and it calls `GetCurrentCultureName()` internally at **line 188** to pick `effectiveTitle`. Option A
therefore REQUIRES a signature change: add a `string? effectiveCultureName = null` parameter and
replace the line-188 `GetCurrentCultureName()` call with the supplied culture (falling back to
`DefaultCultureName`, never `CurrentCulture`). Call sites of `NormalizeTitleLocalizations` are
enumerated in *Files to modify*.

### Decision 3 — Where does the per-environment cache live, and what is the async contract? (M-5/M-6/OQ-05)

The precedent `PlatformVersionResolverFactory.Create()` returns a **fresh** `PlatformVersionResolver`
each call (factory L48), and the 5-minute cache is a private field on that resolver instance. Because
both consumers (`ComponentInfoTool` transient, `ComponentInfoCommand`) call `Create()` per request,
that cache is effectively **cold on every call** — it does nothing across CLI invocations and nothing
across MCP tool calls. Copying that pattern verbatim would make AC-05 ("resolve at most once per
session") unsatisfiable on the server side.

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Per-resolver-instance cache (verbatim copy of PlatformVersionResolver) | Smallest code | Cold on every `Create()` call → AC-05 unmet server-side; FR-10 becomes guidance-only | Rejected: does not satisfy AC-05 |
| B: **Singleton `ICurrentUserCultureCache`, environment-URI-keyed**; resolver reads/writes through it | Survives across MCP tool calls within one server process; resolver stays per-environment & cheap to build; deterministic TTL via injected `TimeProvider` | One extra singleton interface | **Chosen** |
| C: New MCP session-state object | Explicit single source | clio MCP has no per-session container; adds lifecycle complexity | Rejected: over-engineering |

**Chosen design (B).**
- `ICurrentUserCultureCache` is registered as a **singleton** in `BindingsModule.cs`. It holds a
  `ConcurrentDictionary<string, CacheEntry>` keyed by `EnvironmentSettings.Uri` (the same key
  `PlatformVersionResolver` uses), with a `TimeProvider`-driven TTL (default 5 min, aligned with the
  precedent). The injected `TimeProvider` is the DI-registered one (`BindingsModule.cs` L268 →
  `TimeProvider.System`) so cache-TTL unit tests are deterministic with a `FakeTimeProvider` (Mi-6).
- `ICurrentUserCultureResolverFactory.Create(settings)` builds a per-environment
  `CurrentUserCultureResolver` that takes the environment's `IApplicationClient`, `IServiceUrlBuilderFactory`,
  and the **shared singleton `ICurrentUserCultureCache`**. The resolver checks the cache first
  (keyed by `settings.Uri`); on miss it probes `GetApplicationInfo`, validates (Decision 0), stores
  the result, and returns it. Because the cache is the singleton, every `Create()` for the same
  environment URI shares it — so the round-trip happens at most once per environment per TTL window,
  satisfying AC-05/FR-10 across the whole MCP server lifetime.
- **MCP host wiring.** The MCP tool obtains the resolver exactly like `ComponentInfoTool` obtains
  `IPlatformVersionResolver`: inject `ICurrentUserCultureResolverFactory`, resolve
  `EnvironmentSettings` from the per-call args, call `factory.Create(settings)`. The difference from
  the precedent is purely that the cache they all share is the singleton store, so reuse actually
  happens.
- **AC-05 reconciliation.** AC-05 is now satisfied by the server-side cache (no per-entity
  redundant round-trips); the MCP "detect-once" guidance (Story 7) remains as a complementary
  agent-side optimization, not the sole mechanism. FR-10 stays a real server-side capability, not
  demoted to guidance-only.
- **Concurrency (NEW-3).** The check-then-probe-then-`Set` sequence is not atomic across
  concurrent tool calls for the same environment, so two simultaneous first-creations can both
  miss and both round-trip. This is benign (idempotent result, first-write-wins) and matches the
  precedent's own benign race; semantics are **"at most once per TTL under sequential calls;
  duplicate concurrent probe tolerated."** The test plan must NOT assert a strict single-probe
  count under concurrency.
- **Cache key (NEW-5).** The key is intentionally `EnvironmentSettings.Uri` only (matching the
  precedent). Profile culture is per-*user*, so two registered environments that share a URI but
  differ in credentials (same instance, different login) would collide and could serve the wrong
  user's culture. This same-URI-different-user case is rare and explicitly **out of scope**; if it
  ever matters, the key can be extended to `Uri + login` without changing the design.

**M-6 — async contract.** `ICurrentUserCultureResolver.ResolveAsync(CancellationToken)` returns
`Task<CultureResolution>`, matching `IPlatformVersionResolver.ResolveAsync`. The synchronous
`IApplicationClient.ExecutePostRequest` is offloaded via `Task.Run(..., cancellationToken)` (exactly
as `PlatformVersionResolver.TryGetCoreVersionFromApplicationInfoAsync` does, L154-156) so the MCP
host loop is never blocked. The diagnostic CLI verb (`get-user-culture`), which runs synchronously
in `Command.Execute`, calls `ResolveAsync(...).GetAwaiter().GetResult()` — a thin sync bridge at the
single CLI entrypoint, not a second sync overload on the interface.

## Decision 4 — In-scope / out-of-scope caption-emitting verbs (M-3)

Exhaustive table for every creation/mutation verb that can emit a caption/label. "In scope" = the
verb's caption culture changes from host/`en-US` to the effective profile culture and gains the
`--caption-culture` override. The OQ-03 override list, the AC-08 grep file list, and this table are
now identical.

| Verb / class | In/Out | Reason |
|---|---|---|
| `create-entity` / `RemoteEntitySchemaCreator.Create` (L107 write path) | **In** | Primary caption-creation path; FR-03 core target |
| `modify-entity-schema-column` (write) / `UpdateEntitySchemaCommand` caption set (column manager L318/L327 `SetLocalizableValue`) | **In** | Writes a new caption localizable value; FR-03 |
| `create-page` / `PageCreateOptions` (L213,229 `"en-US"`) | **In** | Hardcoded `en-US` caption literal; FR-04 |
| `create-section` / `ApplicationSectionCreateCommand` (caption build) | **In** | Emits section caption; FR-03 |
| `create-app` (section path through `ApplicationSectionCreateCommand`) | **In** | Same caption build as section |
| `create` client-unit schema / `ClientUnitSchemaCreate` (L154,160 `"en-US"`) | **In** | Hardcoded `en-US` caption literal; FR-04 |
| resource-string creation / `ResourceStringHelper` (L71 `"en-US"`) | **In** | Hardcoded `en-US` caption literal; FR-04 |
| schema metadata / `SchemaDesignerHelper.ApplySchemaMetadata` (L132,135 `"en-US"`) | **In** | Hardcoded `en-US` caption literal; FR-04 |
| **column READ/display** — `RemoteEntitySchemaColumnManager.GetColumnProperties` (L114) and `GetSchemaProperties` (L176) | **Out** | READ paths: `GetLocalizableValue` *picks the best display string* from existing localizations (Mi-3). They do not create captions. Decision: read paths stay on host locale (`GetCurrentCultureName`) — they format output for the operator's console, not platform data. Reflected in AC-08 grep expectations (these two lines are allowed to keep `GetCurrentCultureName`). |
| `add-item` / `AddItemCommand` (`--culture`, default `"en-US"`, L69) | **Out** | Already has an explicit `--culture` option that the caller controls; behavior is caller-driven, not host-locale-driven, so it does not have the bug FR-03 targets. Out of scope; no behavior change. (If desired later, its default could resolve from the profile, but the PRD does not require it and it is not in the FR-04 file list.) |
| `create-user-task` / `CreateUserTaskCommand` (`--culture`, default `"en-US"`, L47) | **Out** | Same as `add-item`: explicit caller-controlled `--culture`; not host-locale-derived; not in the FR-04 file list. |
| `data-binding` / process-model bindings (`GenerateProcessModelCommand`, `ProcessModel/Schema.cs`, `ModifyUserTaskParametersCommand`) | **Out** | Process-model / parameter localization is outside the entity-creation caption scope; the `"en-US"` literals there are not entity captions. PRD Non-goal ("unrelated commands that do not emit entity captions"). |
| `schema-sync` | **Out** | No caption emission |
| `create-lookup` | **Out (no separate path)** | Lookups are created through the entity-schema path already covered above; no distinct caption literal of its own |
| `ApplicationInfoService.GetPreferredCultureNames` (L496 `GetCurrentCultureName()`) | **Out (NEW-2)** | READ/display path: builds the preferred-culture probe order for *reading* localized values (`GetDesignLocalizedText`, L458-491), not for writing captions. Same rationale as the column READ paths (Mi-3) — stays on host locale. AC-08 grep file-list is pinned to exclude this line (see AC-08 grep note below). |

**PRD reconciliation.** The PRD Non-goal "will NOT change behavior of unrelated commands that do
not emit captions/labels" is honored: `add-item`/`create-user-task` *do* emit captions but via an
explicit caller-supplied `--culture`, so they are not host-locale-derived and changing them is out
of scope for ENG-91044. This is documented above rather than left ambiguous.

**AC-08 grep specification.** AC-08 ("no caption-culture value derived from `CultureInfo.CurrentCulture`
and no hardcoded `en-US` caption literal except the explicit fallback constant") is verified by
grepping only the **In** files above. The grep MUST treat these lines as the *only* allowed surviving
`GetCurrentCultureName()` / `CurrentCulture` references (READ/display + system-default paths, all Out):
- `RemoteEntitySchemaColumnManager.cs:114, 176` (column READ/display — Mi-3)
- `ApplicationInfoService.cs:496` (`GetPreferredCultureNames` READ probe order — NEW-2)
- `EntitySchemaDesignerSupport.GetCurrentCultureName()` definition itself (the helper stays for READ paths)

Any `GetCurrentCultureName()`/`CurrentCulture`/hardcoded `"en-US"` in an **In** file outside the
`DefaultCultureName` fallback constant is an AC-08 failure. The allow-list above is the pinned
exemption set; adding to it requires a documented Out rationale in Decision 4.

## Decision 5 — Caption script/culture consistency guard (Rev 4, ENG-91044 re-open)

Resolution alone does not guarantee correct output: the caption *text* is authored by the caller
(the agent), and clio cannot synthesize it. The reproduced failure stored Cyrillic text under the
`en-US` key. Two complementary changes close the loop:

1. **Deterministic write guard (`CaptionCultureScriptGuard`).** A stateless static helper (mirroring
   `EntitySchemaDesignerSupport`'s style — pure function, no DI, no `new`) that rejects a caption whose
   letters belong to a script incompatible with its culture key. It is **asymmetric and conservative**:
   it enforces "no non-Latin letters" ONLY for cultures whose language is on a curated **Latin-script
   allow-list** (`en`, `de`, `fr`, …). For any other culture — Cyrillic (`uk-UA`/`ru-RU`), CJK, Arabic,
   or an unrecognised language — it is a no-op, so localized captions and Latin acronyms inside
   non-Latin captions (`"Email адреса"` under `uk-UA`) are never blocked. False-positive rate is zero;
   the exact reproduced case (Cyrillic under `en-US`) and every European Latin profile are caught.
   - **Wiring — every caption/title write path, with the validation culture matched to the ACTUAL
     storage culture of each path.** The cardinal rule (Codex review): validate against the culture the
     value is *stored* under, never a readback-only override.
     - **Localization-map paths → validate each map key against its own value.**
       `EntitySchemaLocalizationContract.NormalizeOptionalLocalizations` is the single funnel for
       `title-localizations`/`description-localizations` shared by `create-entity-schema`,
       `update-entity-schema`, `modify-entity-schema-column`, `create-lookup`, and `sync-schemas`.
       Validates each `culture → value` pair after normalization; immune to any override because the
       keys are explicit. Read/display paths do NOT pass through this contract (same principle as Mi-3).
     - **Effective-culture paths → validate against the resolved effective culture (override > profile >
       `en-US`), because for these the override IS the storage-key culture (the OQ-03 force-language
       knob).** `PageCreateCommand` (create-page) and `SchemaDesignerHelper.ApplySchemaMetadata`
       (create-sql-schema, create-source-code-schema) store the caption keyed by the effective culture,
       and `ClientUnitSchemaCreate` (create-page client-unit) likewise. The guard runs against that same
       effective culture, so a forced `--caption-culture uk-UA` correctly accepts Cyrillic.
     - **Profile-localized scalar paths → validate against the PROFILE culture (`overrideCulture =
       null`), because the value is localized server-side under the profile regardless of any override.**
       `ApplicationSectionCreateService.CreateSection` and `ApplicationSectionUpdateService.UpdateSection`
       (section caption/description) and `ApplicationCreateService.CreateApplication` (application
       name/description). For sections the `--caption-culture` override is readback-only, so validating
       against it would let a mismatched override smuggle the wrong language past the guard (the exact
       Codex finding); `create-app`/`update-app-section` have no override at all. The override is
       therefore deliberately NOT an escape hatch on these paths.
   - **Severity: hard-fail.** Throws `EntitySchemaDesignerException`; on the MCP path the tool's
     existing catch turns it into a structured `ExitCode 1` failure. Guidance-only was rejected: the
     guidance had already failed once (the agent read it and still mis-stored the text), so a
     deterministic backstop independent of prompt-reading is required for "won't happen again".
   - **Why static, not a DI service.** The guard is a pure, dependency-free validation function called
     from deep inside existing static normalization helpers; making it an injected service would force
     a signature change through the same chokepoints with no testability gain. CLIO001 is not triggered
     (no `new` of a behavior class). This matches the precedent set by the `page-sync` semantic
     validators (workspace diary 2026-05, "hard-fail the exact broken pattern, warn on the rest").

2. **Sharpened guidance.** `McpServerInstructions`, `AppModelingGuidanceResource`, the entity/
   application prompts, and the `get-user-culture` tool description now state explicitly that the
   detected culture is the **language of the caption text**, not just the localization key; that the
   conversation/task language never overrides the profile language; and that the mandatory `en-US`
   value must be English. They also announce that clio enforces this on the write path.

**Rejected alternatives for the guard direction.** A *symmetric* guard (also reject Latin under a
Cyrillic culture) was rejected: Latin acronyms/brand names inside non-Latin captions are common and
would produce false positives. An *enforce-by-default* model (treat unknown languages as Latin) was
rejected: a forgotten non-Latin language would wrongly block correct captions. The allow-list model
fails safe — an omitted language is simply not validated.

## Resolved Open Questions

- **OQ-01** — Layer 1 is a **new internal service `ICurrentUserCultureResolver`** (built per
  environment by `ICurrentUserCultureResolverFactory`, cache in a singleton `ICurrentUserCultureCache`)
  reading **`GetApplicationInfo`**. Surfaced **(a)** as a thin diagnostic CLI verb `get-user-culture`
  (alias `profile-language`) and **(b)** as a read-only MCP tool `get-user-culture`. Rationale: AC-01
  and SM-01 require an observable, testable resolution path independent of any third-party MCP server.
- **OQ-03** — `--caption-culture` override: **YES, additive and optional**, on exactly the **In**
  verbs in Decision 4 (`create-entity`, `modify-entity-schema-column`, `create-page`, `create-section`,
  `create-app` section path, client-unit/resource creation flowing through those). Precedence:
  **explicit `--caption-culture` > resolved profile culture > `en-US` constant**. Kebab-case (CLIO001).
- **OQ-04** — culture-not-in-supplied-map rule: the effective (override or resolved) culture is the
  caption key **only when present in the supplied localization map; otherwise fall back to `en-US`**,
  which `NormalizeLocalizationMap` already guarantees present. The effective culture is **never
  injected as a new map entry**. This interacts with **M-4**: if the needed key is already in the
  supplied map, creation does not even need the `GetApplicationInfo` round-trip.
- **OQ-05** — per-session reuse = **server-side singleton, environment-URI-keyed cache** (Decision 3,
  Option B) + complementary MCP-agent detect-once guidance.

## M-4 — GetApplicationInfo is NOT a hard precondition

Creation paths must not abort previously-working scripted/CI flows just because the
`GetApplicationInfo` round-trip fails. The resolution gate is:

```
effectiveCulture =
    --caption-culture (if supplied)                          → use it; SKIP resolution entirely
    else if supplied localization map already has the key    → use en-US per OQ-04; resolution NON-FATAL
    else resolve profile culture:
        Resolved(c)        → use c
        Failed(reason)     → CLI: Error: + non-zero (no override, no usable map entry)
                             MCP: success:false signal → ask-user (FR-06)
```

- **Skip path.** An explicit `--caption-culture` short-circuits resolution: no round-trip, no failure
  surface. CI determinism (OQ-03 escape hatch) is preserved.
- **Non-fatal path.** If the caller already supplied the localized strings the map needs (the common
  scripted case), a resolution failure degrades to `en-US` (OQ-04) instead of aborting — preserving
  FR-13/AC-06 regression safety for scripts that never relied on profile detection.
- **Hard-abort path.** Only when there is neither an override nor a usable map entry does a `Failed`
  resolution become a CLI `Error:` + non-zero (AC-ERR) / MCP ask-user (FR-06). This is the case where
  proceeding would genuinely guess the wrong language.

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/EntitySchemaDesigner/CurrentUserCultureResolver.cs` | `ICurrentUserCultureResolver` + `CurrentUserCultureResolver`: `Task<CultureResolution> ResolveAsync(CancellationToken)`; reads `sysValues.userCulture.displayValue` from `GetApplicationInfo` via `IApplicationClient` (offloaded with `Task.Run`); validates via `CultureInfo.GetCultureInfo` (B-1); reads/writes the shared singleton cache. |
| `clio/Command/EntitySchemaDesigner/CurrentUserCultureResolverFactory.cs` | `ICurrentUserCultureResolverFactory.Create(EnvironmentSettings)` — mirrors `PlatformVersionResolverFactory` (uses `IApplicationClientFactory.CreateEnvironmentClient` + `IServiceUrlBuilderFactory`) but injects the singleton `ICurrentUserCultureCache` + DI `TimeProvider` (Mi-6). |
| `clio/Command/EntitySchemaDesigner/CurrentUserCultureCache.cs` | `ICurrentUserCultureCache` (singleton): `ConcurrentDictionary<string, CacheEntry>` keyed by `EnvironmentSettings.Uri`; `TimeProvider`-driven TTL (M-5). |
| `clio/Command/EntitySchemaDesigner/CultureResolution.cs` | `CultureResolution` record (DTO) + `Resolved`/`Failed` factory methods. |
| `clio/Command/GetUserCultureCommand.cs` | Diagnostic CLI verb `get-user-culture` (alias `profile-language`); calls `ResolveAsync(...).GetAwaiter().GetResult()`; prints resolved culture or `Error: {message}` + non-zero exit. |
| `clio/Command/McpServer/Tools/GetUserCultureTool.cs` | Read-only MCP tool `get-user-culture` returning `{ culture, resolvedFrom, success, reason }`; obtains the resolver via `ICurrentUserCultureResolverFactory` like `ComponentInfoTool` does. |
| `clio.tests/Command/EntitySchemaDesigner/CurrentUserCultureResolverTests.cs` | Unit: parse + validate `displayValue`; malformed → `Failed("userCulture-invalid")`; missing → `Failed("userCulture-missing")`; `userCulture` absent + `primaryCulture` present → `Failed` (Mi-1); cache hit via shared store; `FakeTimeProvider` TTL expiry (Mi-6); failure classes. |
| `clio.tests/Command/GetUserCultureCommandTests.cs` | `BaseCommandTests<GetUserCultureCommandOptions>`. |
| `clio.tests/Command/McpServer/GetUserCultureToolTests.cs` | MCP tool mapping + structured-failure signal. |
| `clio.mcp.e2e/GetUserCultureToolE2ETests.cs` | E2E for the new tool + guidance assertions (NOT in CI — manual). |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs` | **Add `string? effectiveCultureName = null` to `NormalizeTitleLocalizations`** (M-2) and replace the line-188 `GetCurrentCultureName()` with `effectiveCultureName ?? DefaultCultureName` (never `CurrentCulture`). Keep `GetCurrentCultureName()` only for READ/display helpers (column manager out-of-scope paths). `DefaultCultureName` ("en-US") remains the documented creation fallback. |
| `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs` | Inject `ICurrentUserCultureResolverFactory`; compute the **effective culture** once in `Create` (override > resolved > `en-US`, never `CurrentCulture` — M-1). Replace L107 `GetCurrentCultureName()` (write path) with the effective culture; pass it through `CreateColumn` (`CultureName =`, L264). **L536 `Cultures = [GetCurrentCultureName()]` is a schema-level culture ARRAY, not a caption `cultureName`** (Mi-2): set it to the effective creation culture (the language the schema's data is authored in) — add a dedicated test asserting the array contains the effective culture, separate from the caption tests. Honor `--caption-culture`. |
| `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs` | **WRITE path** (`SetLocalizableValue` at L318/L327, used by `UpdateEntitySchemaCommand` caption set): pass the effective culture. **READ/display paths** L114 (`GetColumnProperties`) and L176 (`GetSchemaProperties`) stay on `GetCurrentCultureName()` (Mi-3 — display formatting for the operator's console, not platform data). AC-08 grep allows these two lines to keep `GetCurrentCultureName`. |
| `clio/Command/ClientUnitSchemaCreate.cs` | Replace hardcoded `"en-US"` (L154,160) with the effective culture; `en-US` stays as the fallback constant. |
| `clio/Command/PageCreateOptions.cs` | Replace hardcoded `"en-US"` (L213,229); thread effective culture + `--caption-culture`; `en-US` fallback. |
| `clio/Command/ResourceStringHelper.cs` | `CreateLocalizableEntry`/`CleanAndMerge` (L71) take a `cultureName` argument; `en-US` fallback. |
| `clio/Command/SchemaDesignerHelper.cs` | `ApplySchemaMetadata` (L132,135) takes a `cultureName` argument; `en-US` fallback. |
| `clio/Command/ApplicationSectionCreateCommand.cs` | Resolve & pass effective culture into caption build; `ResolveLocalizedCaption` (L423) keeps `en-US` precedence but uses the effective culture when present in the map. |
| `clio/Command/UpdateEntitySchemaCommand.cs` (L171) | **In (NEW-1)** — `NormalizeTitleLocalizations` caller on the modify-entity-schema path; must pass the effective culture. Without this it silently keeps `en-US` (the new `effectiveCultureName` defaults to `null`). |
| `clio/Command/McpServer/Tools/EntitySchemaTool.cs` (L78, L462) | **In (NEW-1)** — the MCP entity-schema create/update tool calls `NormalizeTitleLocalizations` twice; both must pass the effective culture (CLAUDE.md MCP-maintenance policy makes this mandatory). |
| Call sites of `NormalizeTitleLocalizations` (complete enumeration) | The 8 callers are: `RemoteEntitySchemaCreator.cs:109,231`, `RemoteEntitySchemaColumnManager.cs:241,310`, `UpdateEntitySchemaCommand.cs:171`, `EntitySchemaTool.cs:78,462` — **all In**, pass the effective culture. The helper definition is `EntitySchemaDesignerSupport.cs:173`. No out-of-scope caller exists, so no caller passes `null` in practice. |
| Options classes for the **In** verbs (`CreateEntitySchemaCommand`, `PageCreateOptions`, section/client-unit options) | Add `[Option("caption-culture", …)]` kebab-case override (OQ-03). |
| `clio/BindingsModule.cs` | Register `ICurrentUserCultureCache` as **singleton** (the cross-call cache — M-5); register `ICurrentUserCultureResolverFactory` as singleton (like `IPlatformVersionResolverFactory`, L292). Register `GetUserCultureCommand`. |
| `clio/Program.cs` | Wire the `get-user-culture` verb. |
| `clio/Command/McpServer/McpServerInstructions.cs` | Add detect-once / reuse / ask-on-failure profile-language rule (FR-07). |
| `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs` | Document detect-once / reuse / ask-on-failure (FR-08). |
| `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs`, `PagePrompt.cs`, `ApplicationPrompt.cs` (entity/page/section/application) | Add profile-language detection guidance (FR-09). |
| `clio/help/en/get-user-culture.txt`, `clio/docs/commands/get-user-culture.md`, `clio/Commands.md` | New verb docs (FR-11). |
| `clio/help/en/*.txt`, `clio/docs/commands/*.md` for the **In** verbs | Document `--caption-culture` and the profile-culture behavior change (FR-11). |
| `docs/McpCapabilityMap.md` | Add `get-user-culture` tool; bump counts/snapshot date (FR-12). |

### Key interfaces / contracts

```csharp
// Resolution result — DTO record (created with `new`, allowed for data carriers).
// INVARIANT (NEW-6): consumers MUST branch on `Success` first. On failure `Culture` is
// deliberately set to the `en-US` fallback so the M-4 NON-FATAL path can use it directly,
// but a consumer on the HARD-ABORT path (no override, no usable map key) must NOT read
// `Culture` without first checking `Success` — otherwise the ask-user/Error path is bypassed.
public sealed record CultureResolution(string Culture, bool Success, string? FailureReason)
{
    public static CultureResolution Resolved(string culture) => new(culture, true, null);
    public static CultureResolution Failed(string reason) =>
        new(EntitySchemaDesignerSupport.DefaultCultureName, false, reason);
}

// Async behavior service — matches IPlatformVersionResolver.ResolveAsync (M-6).
public interface ICurrentUserCultureResolver
{
    /// <summary>
    /// Resolves and validates sysValues.userCulture.displayValue from GetApplicationInfo.
    /// Returns Failed(...) for missing/empty/unparseable culture; never throws into the
    /// creation path for a missing field. Reads/writes the shared per-environment cache.
    /// </summary>
    Task<CultureResolution> ResolveAsync(CancellationToken cancellationToken = default);
}

// Environment-bound factory — mirrors IPlatformVersionResolverFactory.
public interface ICurrentUserCultureResolverFactory
{
    ICurrentUserCultureResolver Create(EnvironmentSettings settings);
}

// Singleton cross-call cache keyed by EnvironmentSettings.Uri (M-5).
public interface ICurrentUserCultureCache
{
    bool TryGet(string environmentUri, out CultureResolution resolution);
    void Set(string environmentUri, CultureResolution resolution);
}
```

### CLI flag specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `--caption-culture` | string | No | Override the effective caption culture on the **In** creation commands (precedence: override > profile > `en-US`). Supplying it SKIPS the `GetApplicationInfo` round-trip (M-4). |
| (`get-user-culture`) | verb | — | Diagnostic verb; uses standard `--environment/-e` from `RemoteCommandOptions` |

All flags are kebab-case — CLIO001 enforced. No new camelCase; no renames (no aliases beyond the verb alias `profile-language`).

### Failure behavior (FR-06 / AC-04 / AC-ERR / M-4)

- **CLI**: `get-user-culture` and any **In** creation command that *requires* a resolved culture
  (no `--caption-culture`, no usable map key) and gets a `Failed` resolution prints
  `Error: {user-friendly message}` (no stack trace) and exits non-zero.
- **CLI (non-fatal)**: when `--caption-culture` is supplied or the supplied map already has the key,
  a resolution failure does NOT abort — creation proceeds with the override / `en-US` (M-4).
- **MCP tool**: returns `{ success:false, reason:"<unreachable|unauthorized|userCulture-missing|userCulture-invalid>" }`.
- **MCP guidance**: on `success:false` the agent MUST ask the user which language to use and MUST
  NOT silently fall back to host locale or `en-US`.
- The resolver itself never throws into the creation path for "field missing/invalid"; it returns
  `CultureResolution.Failed(...)` so callers choose CLI-error vs MCP-signal vs non-fatal explicitly.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NSubstitute + `FakeTimeProvider` | resolver parses + **validates** `userCulture.displayValue` (B-1); malformed → `Failed("userCulture-invalid")`; missing/empty → `Failed("userCulture-missing")`; `userCulture` absent + `primaryCulture` present → `Failed` (Mi-1); cache hit via shared singleton store across two `Create()` calls (M-5); TTL expiry with `FakeTimeProvider` (Mi-6); unreachable/unauthorized → `Failed` | `clio.tests/Command/EntitySchemaDesigner/CurrentUserCultureResolverTests.cs` |
| Unit | `BaseCommandTests<TOptions>` | `get-user-culture` prints culture; prints `Error:` + non-zero on failure | `clio.tests/Command/GetUserCultureCommandTests.cs` |
| Unit | NSubstitute | creators/managers use the effective culture (never `CurrentCulture` — M-1); `--caption-culture` precedence; `--caption-culture` skips resolution (M-4); map-has-key skips/non-fatal (M-4/OQ-04); `Cultures` array (Mi-2) = effective culture; column READ paths stay on host locale (Mi-3); `en-US` retained in maps (FR-05/AC-03) | `clio.tests/Command/EntitySchemaDesigner/RemoteEntitySchemaCreatorTests.cs` (+ column manager, page, section, client-unit, resource-string tests) |
| Unit | NSubstitute | `NormalizeTitleLocalizations` honors the new `effectiveCultureName` arg; null → `en-US` (not `CurrentCulture`) | `clio.tests/Command/EntitySchemaDesigner/EntitySchemaDesignerSupportTests.cs` |
| Unit | NSubstitute | MCP tool mapping + structured failure signal | `clio.tests/Command/McpServer/GetUserCultureToolTests.cs` |
| Unit | string assertions | instructions + `app-modeling` resource + 4 prompt families contain detect-once/reuse/ask-on-failure text (SM-03) | `clio.tests/Command/McpServer/*PromptTests.cs`, `*GuidanceResourceTests.cs` |
| E2E | clio.mcp.e2e | real `mcp-server` exposes `get-user-culture`; guidance assertions (AC-07) | `clio.mcp.e2e/GetUserCultureToolE2ETests.cs` |

**MCP E2E is NOT in CI — manual execution only** (per project-context.md).

### Regression-safety design (FR-13 / AC-06) — rewritten for M-1

Regression safety now comes from **`en-US` being the universal fallback**, NOT from reproducing
`CultureInfo.CurrentCulture`:

1. **Creation paths never read `CurrentCulture`.** Every **In** path passes a non-null effective
   culture: `--caption-culture` > resolved profile > `DefaultCultureName` ("en-US"). When resolution
   fails non-fatally (M-4), the effective culture is `en-US` — which is exactly what the hardcoded
   literals produced before, so byte-identical output for the `en-US` case.
2. **`en-US` remains the mandatory map entry** (`NormalizeLocalizationMap` unchanged) — the
   localization-contract validators see no change.
3. **OQ-04 fallback to `en-US`** guarantees that when the resolved culture is not in the supplied
   map, output is byte-identical to today.
4. **READ/display paths unchanged** (Mi-3): column-property display still uses host locale, so
   console output for existing read commands is identical.
5. **Parity unit test**: given profile culture == `en-US` (or resolution `Failed` with a usable map),
   assert the produced `caption`/`description`/`title-localizations` payloads equal the pre-change
   payloads (snapshot comparison) to lock AC-06.

> Note: when the host locale was previously non-`en-US` *and* the profile culture differs, output
> intentionally changes — that is the approved FULL behavior change, not a regression.

### FR / AC → component map

| Requirement | Components / files |
|---|---|
| FR-01, AC-01 | `CurrentUserCultureResolver` (reads + validates `GetApplicationInfo` → `sysValues.userCulture.displayValue`) |
| FR-02, A-05 | resolver uses only `IApplicationClient`; no cliogate, no `HttpClient` (SM-01 counter) |
| FR-03, AC-02, AC-08 | `RemoteEntitySchemaCreator` (write), column manager WRITE path, `PageCreateOptions`, `ApplicationSectionCreateCommand` use effective culture (Decision 4) |
| FR-04, AC-08 | `ClientUnitSchemaCreate`, `PageCreateOptions`, `ResourceStringHelper`, `SchemaDesignerHelper`, `EntitySchemaDesignerSupport` — hardcoded `en-US` replaced, fallback retained |
| FR-05, AC-03 | `NormalizeLocalizationMap` unchanged; OQ-04 `en-US` fallback rule |
| FR-06, AC-04, AC-ERR | `CultureResolution.Failed` → CLI `Error:`+non-zero / MCP `success:false` signal → guidance ask-user; M-4 gating |
| FR-07 | `McpServerInstructions.cs` |
| FR-08 | `AppModelingGuidanceResource.cs` |
| FR-09, AC-07 | `EntitySchemaPrompt.cs`, `PagePrompt.cs`, section/`ApplicationPrompt.cs` |
| FR-10, AC-05 | singleton `ICurrentUserCultureCache` (env-URI-keyed) + agent guidance (Decision 3) |
| FR-11 | `get-user-culture` docs + `--caption-culture` docs across help/en, docs/commands, Commands.md |
| FR-12 | `docs/McpCapabilityMap.md` |
| FR-13, AC-06 | regression-safety design (above) + parity snapshot test in **each In creator story** (4a, 5, 6 — NEW-4) |

### Story-sized chunking (for story-writer)

Dependency edges (Mi-5): **Story 1 → {4, 5, 6}** (creators need the resolver); **Story 1 → 2, 3**
(verb + tool need the resolver); **Story 3 → 7** (guidance references the tool contract).

1. **Story 1 — Culture resolver service + cache (FR-01, FR-02, FR-10, AC-01, AC-05, SM-01).**
   `CultureResolution` + `ICurrentUserCultureCache` (singleton) + `CurrentUserCultureResolver`
   (`ResolveAsync`, validation) + `CurrentUserCultureResolverFactory` + DI registration + unit tests
   (incl. validation B-1, Mi-1, cache M-5, TTL Mi-6). **No dependencies.**
2. **Story 2 — `get-user-culture` CLI verb (OQ-01, FR-11, AC-ERR).** Command + options + Program.cs
   wiring + docs + `BaseCommandTests`. **Depends on Story 1.**
3. **Story 3 — `get-user-culture` MCP tool (OQ-01, FR-12, AC-04 signal).** Tool + structured failure
   signal + `docs/McpCapabilityMap.md` + unit + e2e. **Depends on Story 1.**
4. **Story 4 — Entity-schema creation uses effective culture.** *Split into 4a/4b to avoid
   over-bundling (Mi-5):*
   - **4a** — `RemoteEntitySchemaCreator` write path (L107) + `Cultures` array (L536, Mi-2) +
     `NormalizeTitleLocalizations` signature change (M-2) + `--caption-culture` + parity test (AC-06).
   - **4b** — `RemoteEntitySchemaColumnManager` WRITE path (L318/L327); explicitly leave READ paths
     L114/L176 on host locale (Mi-3) with a test asserting that.
   **Depends on Story 1.**
5. **Story 5 — Page / client-unit / resource creation uses effective culture (FR-03/FR-04, AC-06).**
   `PageCreateOptions` + `ClientUnitSchemaCreate` + `ResourceStringHelper` + `SchemaDesignerHelper`
   + `--caption-culture` + M-4 gating tests + **AC-06 parity test for these creators** (NEW-4:
   AC-06 spans every In path, not only entity-schema). **Depends on Story 1.**
6. **Story 6 — Section / application creation uses effective culture (FR-03/FR-04, AC-06).**
   `ApplicationSectionCreateCommand` + tests + **AC-06 parity test for the section/app caption build**
   (NEW-4). **Depends on Story 1.**
7. **Story 7 — MCP guidance: detect-once / reuse / ask-on-failure (FR-07, FR-08, FR-09, AC-07).**
   `McpServerInstructions` + `AppModelingGuidanceResource` + entity/page/section/application prompts
   + guidance/e2e assertion tests. **Depends on Story 3.**

## Consequences

- **Positive**: generated captions match the connected Creatio user's profile language; culture is
  deterministic and CI-independent of runner locale; the singleton cache makes "resolve once per
  session" real on the server side; resolution is observable/testable via a read-only verb + tool;
  `displayValue` is validated so a malformed culture can never become a caption key; `en-US` contract
  and validators are untouched.
- **Trade-offs**: an extra `GetApplicationInfo` round-trip per environment per TTL window (mitigated
  by the singleton cache, and skipped entirely when `--caption-culture` or a usable map key is
  present); culture must be threaded through several creation paths incl. one new helper-signature
  change (`NormalizeTitleLocalizations`); the `--caption-culture` override adds one optional flag per
  **In** creation command.
- **Breaking change**: **No (signature)**. Behaviorally yes — caption culture source changes from
  host/`en-US` to profile culture for the **In** verbs (the approved FULL change). Scripted flows that
  pass `--caption-culture` or a complete localization map are unaffected (M-4). Note in `RELEASE.md`
  as a behavior change with the `--caption-culture` escape hatch.

## Pre-implementation Checklist

- [ ] All new CLI options are kebab-case (`--caption-culture`, `get-user-culture`)
- [ ] No MediatR — `Command<TOptions>` + constructor-injected services only
- [ ] `userCulture.displayValue` validated via `CultureInfo.GetCultureInfo`; malformed → `Failed` (B-1)
- [ ] Creation paths never read `CultureInfo.CurrentCulture`; effective culture always non-null (M-1)
- [ ] `NormalizeTitleLocalizations` signature change applied + all call sites updated (M-2)
- [ ] In/Out scope matches Decision 4 across OQ-03 / AC-08 grep / docs (M-3)
- [ ] `--caption-culture` or usable map key skips/softens resolution; hard abort only when neither (M-4)
- [ ] `ICurrentUserCultureCache` registered as **singleton**, env-URI-keyed (M-5)
- [ ] Resolver is `Task<CultureResolution> ResolveAsync(CancellationToken)`; sync bridge only at CLI verb (M-6)
- [ ] `Cultures` array (Mi-2) set to effective culture; column READ paths stay host-locale (Mi-3)
- [ ] Cache TTL tests use DI `TimeProvider` / `FakeTimeProvider` (Mi-6)
- [ ] No raw `HttpClient` — `IApplicationClient` only (SM-01 counter)
- [ ] `en-US` remains the named fallback constant and mandatory map entry (FR-05/AC-03)
- [ ] Error messages are user-friendly `Error: {message}`; non-zero exit on CLI hard-fail
- [ ] Parity/regression test locks AC-06
- [ ] MCP tool/prompts/resources updated and covered by `clio.mcp.e2e` (manual)
- [ ] Docs (help/en, docs/commands, Commands.md) + `docs/McpCapabilityMap.md` updated
- [ ] `RELEASE.md` notes the behavior change
