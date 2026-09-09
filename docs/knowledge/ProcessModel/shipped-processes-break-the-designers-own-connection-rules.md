---
description: The shipped 7.8.0 process corpus contains gateway shapes the designer's own connection rules forbid and the runtime nonetheless executes - 45 gateways whose only outgoing flow is a default one, 7 diverging or-gateways carrying a plain sequence flow, 65 with no default at all - so a validator rule derived from the designer's palette or from Academy wording will reject real, running content unless it is measured against the corpus first
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ValidateProcessGraphTool.cs
  - spec/ai-business-process-generation/ai-bp-connection-rules.md
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — the rules the Creatio designer enforces while you DRAW a process are strictly
tighter than the rules its runtime enforces while it RUNS one, and the shipped product itself is full
of the difference. Measured over the 1 711 process schemas in `C:/Projects/PackageStore`:

| Shape the designer will not draw | Shipped instances | Why it runs anyway |
|---|---|---|
| An or-gateway whose ONLY outgoing flow is a `default` one | 45 (40 exclusive, 5 inclusive) | A converging gateway has one way out; the designer's palette for an or-gateway offers conditional and default only, so the single flow is a default by construction |
| A DIVERGING or-gateway carrying a plain `sequence` flow | 7 | `FlowConditionalGateway.GetIsDefSequenceFlow` treats any outgoing that is not a conditional flow as the default branch |
| A diverging or-gateway with conditional flows and NO default | 65 | Legal; it throws only when no condition matches at run time |
| A default flow with no conditional sibling, whose plain sibling leads into a gateway | 1 (`CrtLeadOppMgmtApp/LeadDistribution`, `ReadDataUserTask1`) | `ProcessSchemaFlowNode.GetOutgoingsDefFlows` recurses into a sequence flow whose target is a gateway and collects THAT gateway's defaults |

The named counter-examples are worth keeping because they are the fixtures: `Compensation/BonusVisaBaseSubProcess`,
`CrtOpportunityManagement/Presentation780`, `LeadFinance/LeadManagementFinance`,
`OldGoogleIntegration/SynchronizeWithGoogleModuleProcess`, `OpportunityBank/Presentation780Finance`,
`PRMBase/CreateOrUpdatePartnerParamHistory`, `BulkFileManagement/DeleteFilesInTable`,
`CaseService/RunSendEmailToCaseGroup`.

**Why it is this way** — the designer's connection rules are a UI affordance added later than the
runtime, and older versions of it drew shapes the current one refuses. Academy documents the designer's
rule ("a default flow is used when there is at least one conditional flow outgoing from the same
process element") and simply does not contemplate the converging gateway its own tool produces. Nothing
migrated the existing content, because the content works.

**What breaks if you ignore it** — a validator rule written from the designer's palette, from Academy,
or from the BPMN specification is *reachable on real input* and turns into a refusal of shipped
product. The route is not exotic: `describe-business-process` a stock process, feed its graph to
`validate-process-graph`, and an agent is told the platform's own content is invalid. It then "fixes"
a process that was never broken.

This happened twice in one change, in opposite directions, which is why the table above is worth more
than the rules it qualifies:

- R14 unscoped rejected all 45 of the first row. Arity-scoping it took that to 1, not 0 — the last one
  needed the `GetOutgoingsDefFlows` recursion in the fourth row.
- R7/R9's "a diverging gateway must not carry a plain sequence flow" shipped as an ERROR and rejected
  all 7 of the second row. It is a warning now.

**Measure before you add a rule.** The corpus is local and a scan is a few minutes: walk
`*/Schemas/*/metadata.json`, collect `BK4` recursively, and read `BL1` for the CLR class and `CI4` for
the `FlowType` (1 = Default, 2 = Conditional, absent = plain). A rule that no shipped process violates
is safe as an error; anything else is a warning, or it is scoped until it is.
