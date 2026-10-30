# ENG-95985 Send email attachments: implementation plan

Written 2026-10-28. Phase 1 only: research, design and plan. As of the plan (2026-10-28), no product code had been
written and nothing had been written to the stand, Jira, GitHub or GHE. The owner agreed all twenty decisions on 2026-10-28 (decisions file).

Jira: ENG-95985 "Send email attachments" (Story, epic ENG-92704 "Create BP via AI Toolkit", In Progress, sprint
"BPMS Tools 351"). Its blockers are closed: ENG-92719 "File processing element" (with ENG-95984, ENG-96505, ENG-96506,
ENG-103937) and ENG-95986 "Support template message mode in the Send Email element".

Basis: crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio `origin/master` `358e9db52`,
clio-knowledge `origin/master` `534e913`, CrtProcessDesigner 7.8.0 and the PackageStore corpus, core `TSBpm` trunk,
the Academy, and a read-only inventory of stand `Creatio` taken on 2026-10-28. Research notes r0-r10 (local, not part of
the plan) are the lead's working notes and are not in the repository: citations of the form `R/...`, `r0`-`r10`
or `eng-95985-research` in this folder point at those local notes and are kept only as an evidence trail; every
claim the plan relies on is restated in this folder, and rows whose only evidence is a local note say so.

Path alias `PD/`: it means the PackageStore `CrtProcessDesigner/branches/7.8.0/`. A path written `PD/Schemas/...`
(facts, measurements, serialization) already includes the `Schemas/` segment; the files whose legend defines `PD/` as
`.../7.8.0/Schemas/` (decisions, design, test plan, the SPEC and the ADR) write the schema folder directly
after `PD/`.

## 1. Short answer

- **The feature is one designer row per attachment.** An attachment in the Send email element is an element
  parameter `Attachments<N>` (a collection) with one nested item `Attachments<N>FileLocator` (a file). The run time
  finds rows by the name pattern `^Attachments\d+$` AND by `CreatedInSchemaUId` = the process schema, and reads each
  row's `<slot>FileLocator`. The package must write exactly this shape: a wrong stamp, a wrong item name or a missing
  outer binding saves green and then sends nothing or fails at run time.
- **All four mode combinations share one code path** at run time (manual/auto x custom/template). Manual mode copies
  the files to the draft email activity (`ActivityFile`), so it can be proven on a stand without a mailbox; automatic
  mode is proven by real delivery on stand `Creatio` once the owner configures mail sending there (EM7, Q17).
- **The refinement comment (2026-09-08) is wrong on five points**: the outer slot IS bound for a collection source;
  both data value types have UId constants; `ProcessParameterService` needs no change (element-owned slots never pass
  its allow-list); the feature is seeded through `AdminUnitFeatureState`, not `Feature.DefaultState`, and it also gates
  the RUN TIME (a rollout toggle on everywhere since 7.17.1, so the build does not check it: Q9);
  `LoadAttachmentsInSameProcess` is an Exchange sync feature, unrelated. Details: facts file §8.
- **Most of the machinery exists.** The Process file element's `files` source planner, `BindCollection`, the
  two-level binding rules, the binding describer and the R6 refusal pattern are reused. New: the slot writer, the
  block, the replace rule, raw-route guards, a build-path order guard, a multi-instance refusal, describe read-back,
  notices.
- **Two things gate the work** now that the owner's answers are in (2026-10-28): a designer capture on the current
  platform (EM1, the AC7 oracle: the corpus has only four one-row bindings from 2021-2022), and the residual-risk STOP
  gate after the first slice (EM2/EM3: the pre-save gate and the designer accept a server-built slot).
- **Budgets are at the ceiling**: clio create/modify descriptions have about 2-3 bytes free, `process-modeling.md`
  1 byte. Every added sentence is paid by a swap (Q12).

## 2. Files

| File | What it holds |
|---|---|
| [decisions](eng-95985-send-email-attachments-decisions.md) | Q1-Q20: options, facts, the agreed answer (2026-10-28), Jira AC wording |
| [facts](eng-95985-send-email-attachments-facts.md) | what is true today in the package, designer, run time, corpus, stand, Academy, clio and KB; refinement verdicts |
| [serialization](eng-95985-send-email-attachments-serialization.md) | the target bytes key by key, binding per source kind, invariants, parity method, what EM1 records |
| [design](eng-95985-send-email-attachments-design.md) | contract, modify semantics, package components C1-C10 (C8 dropped with Q9), messages, clio K1-K12, guidance |
| [measurements](eng-95985-send-email-attachments-measurements.md) | stand probes EM0-EM15: question, gate, steps, expected result, cleanup |
| [plan](eng-95985-send-email-attachments-plan.md) | critical path, phases, slices, PR split, cuts, merge order, gates, DoD, risks |
| [test plan](eng-95985-send-email-attachments-test-plan.md) | coverage matrix, unit/parity/clio/e2e/KB tests, manual Jira TCs |

## 3. Acceptance criteria -> plan

| AC | Design | Proof |
|---|---|---|
| AC1 block + describe | `email.attachments[]` (Q1), describe in the write shape (Q11) | unit round trip; EM2, EM3 |
| AC2 three variants, one collection to many emails | any source holding files (Q2); item found by type | unit per source kind; EM4, EM4c, EM11 |
| AC3 more than one attachment | one slot per entry; any rows per slot | unit; EM4 |
| AC4 manual/auto x custom/template | no mode-specific code; shared run-time path | unit x4; EM4, EM5, EM7 on stand `Creatio` after the owner configures mail (Q17) |
| AC5 clear error naming the source | refusals of design §5.1 (Q6, Q20) and the reachability warning | unit per refusal; e2e refusal creates nothing |
| AC6 ordering rule | guidance states configure-first and flow-before; build guard (Q7) | KB tests; EM10 |
| AC7 serialization parity, tests, docs, MCP | parity patterns A/B/C; K1-K12; new article | parity suite; e2e Ignored = 0 |

## 4. Decisions (agreed 2026-10-28)

The owner agreed all twenty on 2026-10-28; Q9, Q16 and Q17 changed from the first recommendation. Q1 member
`email.attachments`, entry = `fileProcessing.files` shape. Q2 any source that holds files. Q3 accept a single File
with a warning (EM8 confirms). Q4 replace semantics (removal notice carries what was removed). Q5 lowest free number,
counted over every parameter of the element. Q6 refuse no/unknown/fileless/nested/later-declared sources; warn when
the source is not on a flow path before the email. Q7 guidance + build guard for attachments only. Q8 refuse every
non-block mapping onto slots (guard in the shared `Plan` under `!fromBlock`, block form of `BindCollection`). Q9
(changed) no build-time check of `UseProcessEmailAttachments`: a rollout toggle on everywhere since 7.17.1; the KB
article keeps one troubleshooting line (an email without its files: check the feature for the user who runs the
process). Q10 refuse mapping from a slot. Q11 describe `email.attachments[]` in the write shape, plus slot,
`bindingLevels` and issues. Q12 swap for bytes (no ceiling raise), new KB article `process-send-email-attachments`.
Q13 claim comment on ENG-95985; merge package -> KB -> clio. Q14 docs-only BMAD clio PR first. Q15 fix the
`TakesFiles` comment (Creatio.ai stays out of scope). Q16 (changed) refuse `email.attachments` on a multi-instance
Send email, which the designer can make (its slots would become nested items the run time does not see, traced);
describe reports such an element with an issue; one file per recipient stays the multi-instance sub-process pattern,
documented only if EM8 passes; a `removeElement` guard only if EM11 shows a silent break. Q17 (changed) the owner
configures real mail sending on stand `Creatio`; EM7 runs there (auto + custom, auto + template, the Create activity
variant with `ActivityFile` rows); EM6 is dropped. Q18 the lead drives the designer captures in Claude in Chrome, the
owner logs in. Q19 the lead writes the Jira AC (description only, text approved by the owner first). Q20 refuse a Process file source with no record scope and no
filter, for Object attachments and Generated report, judged at the end of the request (files are read with system
rights and sent out; the designer has no such check).

## 5. Rules this plan follows

- No future CrtProcessBuilder cut number appears in these files; numbers live in the claim comment, the tag and
  `descriptor.json`. Versions appear only as history ("measured on 1.6.6.125").
- The stand is named only as `Creatio` (a registered clio environment); its address is not written here.
- A source trace is a hypothesis: claims tagged "traced, not measured" carry the EM that settles them.
- Every stand write, Jira write, push, reviewer request and merge waits for the owner's explicit word; merges are
  human.
