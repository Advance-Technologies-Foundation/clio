# ADR: The bundled archive is the source of truth for a bundled package's version

**Status:** accepted · **Supersedes** the version half of
[adr-deliver-process-builder-package.md](adr-deliver-process-builder-package.md) · [ENG-94385](https://creatio.atlassian.net/browse/ENG-94385)

## Context

clio ships `CrtProcessBuilder` inside its own distribution and gates five process-designer commands on it.
As first built, one hand-maintained constant — `BundledPackages.ProcessBuilderVersion` — served three
different purposes at once:

1. the version clio SHIPS (printed by `clio info`, pinned against the archive by a guard test);
2. the minimum version each of the five commands REQUIRES, as the `[RequiresPackage]` argument;
3. the version every environment is expected to CONVERGE to, because (2) equalling (1) is what makes a
   gated command refuse an out-of-date environment and thereby trigger an install.

Collapsing them worked only because the three numbers happened to coincide. Two things forced the question:

- **The product decision** (approved) is that an agent working a user's business task installs and updates
  the package without asking, exactly as it does for `cliogate`. Under that model, convergence is the goal —
  every environment should carry what the distribution carries — not a cost to be minimised.
- **A copy can disagree with its original, and did.** The archive is a content file copied to the build
  output, not compiled in; `clio compress -d` replaces it without recompiling anything. During this work
  three build outputs held three different archives while the constant held one value. A constant in the
  assembly cannot describe a property of the distribution.

## Decision

Separate the three concepts. The archive's descriptor is the only place the version lives.

### 1. Fact — what the distribution carries

A new `IBundledPackageCatalog` resolves a bundled package's version by reading the descriptor out of the
`.gz` **in the build output**, i.e. the same path the install command resolves. Cached for the process
lifetime; the archive cannot change under a running process.

It is the only reader of that fact. `clio info`, the convergence check and the install command all go
through it.

### 2. Requirement — what the code needs

`[RequiresPackage]` keeps its present shape and meaning: a declaration by the CODE of what it needs.

```csharp
[RequiresPackage("CrtProcessBuilder")]              // presence — what all five commands need today
[RequiresPackage("CrtProcessBuilder", "1.2.0.0")]   // a literal, added by whoever starts calling something new
```

No command among the five needs a specific version today: each fails only because the package is absent
entirely. A literal appears in the commit that creates the need — the same discipline `cliogate` has used
for years, where one command requires `2.0.0.41`, two require `2.0.0.42`, and four require only presence.

The seven `cliogate` declarations are untouched.

### 3. Policy — convergence

A separate rule in its own class: an environment carrying an OLDER version than the distribution has not
converged. A package is subject to it exactly when clio ships that package — the catalog answers that, so
nothing is declared anywhere.

**What convergence produces is a refusal naming the install verb, not an install.** clio does not run a
configuration build and restart a live instance as a side effect of an unrelated command. The remediation
is driven the way it already is and the way the approved flow describes: the agent reads the refusal and
calls `install-process-builder`. This is deliberately the same observable behaviour users have today — what
changes is where the number comes from, not what happens.

Convergence is evaluated wherever a declared requirement is, so it reaches both chokepoints without either
of them changing, and it inherits the zero-cost guarantee: a command that declares no requirement on a
bundled package fetches no package list.

Requirement and policy can legitimately disagree, and that is the point: with a requirement of `1.2.0.0`,
a distribution carrying `1.5.0.0` and an environment on `1.3.0.0`, the environment is compatible but not
converged. The two produce different messages — "this command needs at least 1.2.0.0" versus "clio carries
1.5.0.0, this environment has 1.3.0.0" — because the reader's next action is the same but the reason is
not. One constant can express neither separately.

### The invariant that replaces the version pin

The old pin asserted `descriptor version == BundledPackages.ProcessBuilderVersion`. With the constant gone
there is nothing to compare, and in its place goes a statement that is actually worth making:

> the distribution must carry a version at least as high as every literal requirement declared against
> that package

Otherwise clio demands of an environment something it cannot itself supply — it refuses and cannot heal.
The check reads the version from the archive and compares it against every `[RequiresPackage]` literal
found by reflection — class-level AND property-level, mirroring what the checker actually enforces — and
asserts the scan itself finds the known declarations, so "found nothing" cannot pass as "nothing to find".

A separate, test-side `ExpectedArchiveVersion` pin remains beside the SHA-256 pin. It is **not** the deleted
constant returning: no production code reads it and nothing compares against it at runtime. It buys what the
SHA pin buys — a `.gz` renders in a diff as a changed byte count, so without a line stating the version a
reviewer cannot see whether it moved, and "did the version move" is now the question that decides whether
every existing environment upgrades. It cannot catch a deliberately frozen version; the rebundle script's
must-increase guard does that.

## Alternatives considered

**Keep the hand-maintained constant** (the state before this ADR). Rejected: it is a copy of a fact that
already exists in the archive, it requires a guard test purely to detect its own drift, and it makes
"should the floor move?" a recurring human decision on every rebundle — a decision that under the approved
product model has only one answer.

**Generate the constant at build time from the archive.** Attributes keep working unchanged and nobody
types the value, so the drift class disappears within a build. Rejected on two grounds. It still creates a
copy, so the fact does not have a single home; and it describes the archive present at COMPILE time while
the install ships the archive present in the build output at RUN time — precisely the divergence observed
during this work. A generated constant would have reported the built version while the install shipped a
different one.

**Add `RequireBundledVersion = true` to `[RequiresPackage]`.** Considered and rejected while writing this
ADR. The attribute declares requirements of the CODE; "whatever the distribution happens to carry" is a
delivery policy, not a property of the code. Putting it in the attribute would merge two kinds of statement
into one construct and lose the ability for requirement and policy to differ.

**Make the floor configurable.** Rejected. The floor states what THIS clio build needs in order to work,
which is identical for every user of that build — nothing varies per deployment, so nothing belongs in
configuration. A user-lowerable floor would also switch off the diagnostic it exists to provide, turning an
early legible refusal back into the late opaque server error. If an escape hatch is ever wanted it should
be an explicit, loud, per-invocation override, not a setting.

## Consequences

**The version fact has one home** and cannot disagree with itself, including in the case that motivated
this ADR: a build output holding an archive other than the repository's. `clio info` and the gate now
describe the bytes that will actually be installed.

**Nobody maintains a version constant**, and the question "who raises the floor, and when" disappears with
the floor. CI drops in an archive; the behaviour follows.

**Convergence becomes automatic**, which is the approved model — and the honest converse must be recorded
here rather than discovered later: **a regression propagates automatically too.** With a hand-maintained
floor one could ship bytes without forcing anyone onto them; with automatic convergence every environment
takes the next version on first use. The mitigations are the package's own test suite before release and
the backup the install takes, not a human gate.

**Every rebundle must raise the version, and the reason to skip it has disappeared.** Convergence compares
versions, so a rebundle that re-packs changed sources under an unchanged version reaches only new installs
and anyone who reinstalls by hand — existing environments stay on the old code silently. (The archive still
installs correctly: `ModifiedOnUtc` differing is what makes the platform rewrite the `SysPackage` row at
all, and it rewrites it with the same version.) Under the previous design there was a real reason to hold
the version back — raising it moved the floor and refused every environment until upgraded, so it was
reserved for contract changes. That cost is gone with the floor, which inverts the guidance: bump on every
rebundle. `rebundle-process-builder.ps1` therefore requires `-Version`, and its `-RaiseFloor` switch is
removed as meaningless.

**The install stays destructive in fact**, whatever the client's prompting policy: it runs a configuration
build and restarts a live instance. The `Destructive = true` annotation describes what the tool does, not
how much friction is desired; silence in the approved flow comes from the client's permission policy. This
ADR does not change that annotation.

**Nothing bounds a version from ABOVE, and that is a deliberate gap rather than an oversight.** Every
mechanism here states a minimum: `[RequiresPackage]` is `installed >= required`, and convergence pulls an
environment forward only. So an OLD clio talking to an environment carrying a NEWER package proceeds
silently — the presence requirement is met and convergence has nothing to say, because refusing would
amount to telling the user to downgrade.

That is correct while the package's service contract stays additive, and it is reachable in ordinary use:
the package ships inside clio, so an environment gets ahead exactly when someone with a newer clio
installed it there — a shared stand with a team on mixed clio versions. What is uncovered is an
INCOMPATIBLE change, where the old clio sends something the new server rejects and the user sees a
server-side error instead of a clio refusal.

Not closed here, for two reasons. `cliogate` has the identical shape after years —
`Program.CheckApiVersion` nudges only when clio's copy is the newer one — so this is the established
behaviour rather than a regression introduced by this ADR. And the fix does not belong on the clio side: an
upper bound would be a constant about package versions that do not exist yet. Only the package knows when
it broke compatibility, so if this ever bites, the package should state a minimum clio version of its own.
Unlike the shipped-version question this ADR settles, that value is genuinely hand-authored — there is
nothing for it to duplicate — so the objection that killed "let the package report its own version" does
not apply to it.

**What this does NOT address.** The outcome check remains liveness-only: a failed UPGRADE leaves the
previous assembly answering `Ping`, so it passes. That limit is orthogonal to the version model and is
recorded in the superseded ADR.

## Open questions

- **Should convergence also cover the absent case**, so a gated command never refuses and the agent simply
  installs and proceeds? It is closer to the approved model — a refusal there carries no information the
  agent can act on differently, only an extra round trip. Left open because it changes the observable
  contract of five commands and their tests.
- **Whether `cliogate` should adopt the same catalog.** It has the same shape — a bundled archive, a
  version, and a soft nudge (`Program.CheckApiVersion`). Out of scope here; noted so the asymmetry is
  deliberate rather than forgotten. Its version-shaped values are analysed in exactly one place, the remarks
  on `clio/Common/BundledPackages.cs`; do not restate them here or anywhere else.
