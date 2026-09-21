# Task: <name>

## Outcome and scope
Original user request and subsequent steering; user-visible outcome, explicit exclusions, reference evidence, checkout and branch.

## Acceptance
Observable success, failure, cancellation and compatibility behavior; required real integration proof.

## Early QA
QA identity, medium effort, acceptance gaps and agreed verification before coding.

## Frozen contract
Operation name/arguments/result/error codes; primitive interface and DTO; capability name and lifetime. Mark which existing architectural decision permits the extension.

## Assignments and dependencies
| Task | Agent/role and effort | Owned files | Depends on | Required checks | State |
|---|---|---|---|---|---|

TeamLead owns shared integration files. One build slot for shared outputs. Record dispatch/handoff and corrections as they occur; do not reconstruct a fictional schedule afterward.

## Decisions and escalations
Concrete conflict, options, Architect/Claude evidence where needed, human decision and what can proceed independently.

## Verification
Exact commands, exit/result, artifacts, proof boundaries. Distinguish worker reports from TeamLead-confirmed results.

## Worker acceptance gates
For each delivery: worker ready-for-review evidence, exact scope, nonauthor reviewer, effort/reason, findings, correction and independent closure, TeamLead acceptance. Shared file visibility is not acceptance.

## Final QA
Independent QA identity, high effort, integrated acceptance evidence, failures and confirmed corrections.

## Independent review
Reviewer IDs, exact reviewed files and commit/diff state, findings, disposition and correction/recheck evidence. Hold the reviewed scope unchanged until verdict; subsequent changes require scoped recheck by a non-author reviewer. No self-approval; independent reviewers verify proposed rejection or closure of findings.

## Delivery
Acceptance checklist, remaining limitations and KISS check.
