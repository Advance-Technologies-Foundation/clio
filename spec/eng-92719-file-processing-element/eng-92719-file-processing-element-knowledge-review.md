# ENG-92719 File processing element: knowledge review of the merged clio PRs

The `make check-knowledge` review that closes the knowledge-record items of story 1 (clio #1841, ENG-96505 Element
readiness and object attachments mode) and story 2 (clio #1847, ENG-96506 Generated report + process parameter
modes). Done on 2026-10-20.

**Method.** `scripts/check-knowledge-applies-to.py` intersected every record's `applies-to` with each merge commit's
first-parent diff: #1841 = `24bfa471b^1..24bfa471b` (103 records, counted on the records as of that merge), #1847 =
`ea3aaf580^1..ea3aaf580` (109 records). Each record's claims were read against clio `origin/master` `ea3aaf580` and,
for claims about the bundled package, against crt-process-builder tag `crtprocessbuilder-1.6.6.109` (the archive
master bundles) and the CrtProcessDesigner 7.8.0 sources. A record was updated only where a claim was false on master.
A record whose `applies-to` was touched only through a shared path and whose claims the change does not reach was left
as it is.

**Counts.** #1841: 96 kept, 6 updated, 1 already deleted by #1847 as planned. #1847: 104 kept,
5 updated, 0 deleted. 6 records updated in all; this review deletes none. The M26 record
`platform/after-saving-object-files-points-at-the-source-files.md` is new in the same change and is not part of the
review.

Columns #1841 and #1847: `x` = the script lists the record for that PR's diff.

| Record (`docs/knowledge/`) | #1841 | #1847 | Verdict | Reason |
|---|---|---|---|---|
| `Command/a-missing-operation-floor-needs-a-version-literal-not-presence.md` | x | x | kept | floor literals moved; the rule (a new operation needs a literal) is unchanged |
| `Command/describe-process-output-is-capped-by-the-described-dtos.md` | x | x | kept | the new fileProcessing DTOs carry bags; the membership list is owned by described-filter-types (recounted) |
| `Common/a-bundled-archive-must-be-packed-from-the-commit-not-the-working-tree.md` | x | x | kept | pins moved with the rebundles; the packing recipe is unchanged |
| `Common/a-presence-only-requirespackage-still-takes-a-convergence-refusal.md` | x | x | UPDATED | its table gave the Create/Modify/ModifyAsNewVersion floors as 1.6.6.14; master has 1.6.6.109, so the cells now point at the attribute |
| `Common/a-version-floor-in-a-caller-facing-message-is-unreachable-advice.md` | x | x | kept | the describe tool and prompt additions name no package version; FlowLabelSurfaces_ShouldNameNoPackageVersion still guards |
| `Common/requirespackage-version-floor-survives-convergence-degraded-modes.md` | x | x | UPDATED | 'that state has ended' was false: both PRs raised the floors to the cut they bundled (1.6.6.103, then 1.6.6.109) |
| `McpServer/a-cli-verb-and-its-mcp-tool-share-one-name.md` | x | x | kept | no tool or verb added, renamed or removed |
| `McpServer/a-named-contract-lookup-is-fitted-so-full-content-needs-detail-full.md` | x | x | UPDATED | headroom figures (2 B, 225 B) stale; master: modify 35 016 of 35 072 B, wire bundle 18 815 of 19 000 B (#1847) |
| `McpServer/a-null-args-object-never-reaches-a-long-tail-tool.md` | x | x | kept | directory prefix; argument binding unchanged |
| `McpServer/an-optional-positional-json-value-loses-its-description.md` | x | x | kept | Operations and PackageName are still init properties |
| `McpServer/counted-claims-in-shipped-text-have-no-drift-oracle.md` | x | x | kept | describe still says 'with four exceptions' |
| `McpServer/emitted-schema-required-comes-from-the-record-stj-binds.md` | x | x | kept | no args record changed |
| `McpServer/first-sentence-of-a-description-becomes-the-compact-index-purpose.md` | x | x | kept | served purposes byte-identical across #1841 (its MCP review); #1847 changed no first sentence |
| `McpServer/flat-argument-classifier-does-not-see-wrapped-payloads.md` | x | x | kept | no residency or filter change |
| `McpServer/mcp-capability-map-has-no-automated-guard.md` | x | x | kept | the map moved in step; the two pinned sentences are still the only guards |
| `McpServer/tool-contract-index-is-lines-by-default-objects-on-request.md` | x | x | kept | index shape unchanged; no tool added |
| `ProcessModel/a-file-collection-is-described-by-its-alias.md` | x | x | kept | process-parameter describe unchanged; the file outputs are element outputs |
| `ProcessModel/a-hand-written-meta-path-saves-green-on-an-unread-column.md` | x | x | kept | MetaPathTokenReference now also checks the Process file value sources ('a recordId'); the not-covered list is unchanged |
| `ProcessModel/a-merge-sits-on-the-row-of-the-split-it-closes.md` | x | x | kept | layout code unchanged from 1.6.6.99 to 1.6.6.109 |
| `ProcessModel/branch-precedence-is-array-order.md` | x | x | kept | flow order and layout lanes unchanged |
| `ProcessModel/build-path-conditions-name-their-parameters.md` | x | x | kept | create dropped a redundant sentence; the expansion is unchanged |
| `ProcessModel/change-access-rights-runs-green-and-changes-nothing.md` | x | x | kept | the tools now say ONE configuration builds green and changes nothing, as its table does; its RELEASE GATE paragraph has been stale since ENG-92717 shipped, not because of these PRs |
| `ProcessModel/conditional-flow-condition-lives-in-two-places.md` | x | x | kept | describe still reports branchesOnActivityResult |
| `ProcessModel/conditional-flow-rekind-must-be-in-place.md` | x | x | kept | re-kind path unchanged |
| `ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md` | x | x | kept | version facts unchanged |
| `ProcessModel/describe-reports-the-diagram-not-only-the-graph.md` | x | x | kept | diagram members unchanged |
| `ProcessModel/described-filter-types-have-no-json-overflow-bag.md` | x | x | kept | recounted on master: 28 types with a bag, 9 without, as #1847 left it |
| `ProcessModel/flow-kind-is-four-fields.md` | x | x | kept | flow writing unchanged |
| `ProcessModel/flow-palette-item-is-set-on-every-shipped-flow.md` | x | x | kept | flow writing unchanged |
| `ProcessModel/lookup-display-name-respects-the-callers-read-rights.md` | x | x | kept | the Process file record's name comes from EnsureRecordOfObjectExists (rights-aware FetchFromDB) at 1.6.6.109; describe reads the stored display macro |
| `ProcessModel/process-builder-drops-undeclared-request-keys.md` | x | x | kept | fileProcessing key sets added and held to the archive by KeySets_Should_EqualTheMembersOfTheBundledServerContracts |
| `ProcessModel/process-file-sysfile-storage-is-refused-until-sf.md` | x | x | kept | SysFileStorageSupported = false, the refusal text and the describe issue unchanged at 1.6.6.109; temporary until SF |
| `ProcessModel/process-formula-conversion-retypes-numeric-constants-as-decimal.md` | x | x | kept | its E2E exists; formulas unchanged |
| `ProcessModel/process-validation-and-runtime-parse-a-filter-reference-differently.md` | x | x | kept | FilterExpressionReference still takes only the canonical token |
| `ProcessModel/process-write-tools-always-read-back-for-the-digest.md` | x | x | kept | only the floor comments changed in the commands |
| `ProcessModel/report-and-parameter-file-elements-stay-generic-until-eng-96506.md` | x | - | deleted by #1847 (planned) | temporary record written by #1841; #1847 registered both variants and deleted it |
| `ProcessModel/shipped-processes-break-the-designers-own-connection-rules.md` | x | x | kept | validator rules unchanged |
| `ProcessModel/subprocess-build-token-needs-a-managermap-arm.md` | x | - | UPDATED | 'one release before any package emits them' was past: 1.6.6.103 builds both and describe emits only the token (the Schema.cs comment #1841 changed) |
| `ProcessModel/the-second-default-refusal-is-what-a-gateway-caller-gets.md` | x | x | kept | flow-kind guards unchanged |
| `ProcessModel/version-family-package-names-need-a-second-query.md` | x | x | kept | version reader unchanged |
| `Tests/merge-before-choosing-a-version-number.md` | x | x | kept | rule unchanged |
| `Tests/no-mcp-e2e-fixture-may-restart-or-recompile-the-shared-instance.md` | x | x | kept | the new fixtures neither restart nor compile |
| `Tests/only-the-noenvironment-tier-of-mcp-e2e-runs-on-github.md` | x | x | kept | CI lanes unchanged |
| `Tests/reachability-not-corpus-absence-decides-whether-a-guard-stays.md` | x | x | kept | method record; directory prefix |
| `Tests/settings-overload-forces-default-string-in-nsubstitute-assertions.md` | x | x | kept | directory prefix; no overload added |
| `Tests/three-defences-and-no-oracle-is-an-unpinned-rule.md` | x | x | kept | R18 and its tests unchanged |
| `infra/ghe-cli-process-builder-repo-name-must-not-be-reused.md` | - | x | kept | sprint-status.yaml still holds its three old-name references |
| `infra/mcp-e2e-processdesigner-category-is-consumed-only-by-teamcity-job-args.md` | x | x | UPDATED | 'the five fixtures' and '59 tests today' false: 22 fixture files carry the category on master, and each PR added one |
| `infra/the-creatio-sandbox-has-five-word-printables-and-no-fastreport.md` | - | x | kept | written by #1847; stand fact |
| `platform/a-collection-row-is-rebuilt-from-the-target-items.md` | x | x | kept | platform reader; 1.6.6.109 describes outerOnly as failing at run time, as the record says |
| `platform/a-connectors-exit-side-is-guessed-from-top-left-corners.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-design-session-serves-unsaved-resources-from-the-shared-manager.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-duplicate-schema-resource-key-overwrites-silently.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-file-copy-without-a-link-column-lands-on-the-source-record.md` | x | x | kept | the saveTo link-column refusals still hold at 1.6.6.109 |
| `platform/a-flow-rekind-does-not-orphan-its-label.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-nested-only-files-binding-reads-one-row.md` | - | x | kept | written by #1847; a single File still binds Files.File only |
| `platform/a-process-file-report-runs-only-word-and-fastreport.md` | - | x | kept | written by #1847 |
| `platform/a-process-resource-cache-survives-its-own-save.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-read-data-column-selection-leaves-an-unlisted-mapped-column-empty.md` | x | x | kept | RecordColumnReference and ReadDataConfigApplier keep their role at 1.6.6.109 (only the sort codec was extracted) |
| `platform/a-result-enumerating-connector-is-edited-as-a-selection-not-a-formula.md` | x | x | kept | the setFlowCondition refusal's remedy was reworded; the record does not quote it |
| `platform/a-schema-registry-lookup-holds-a-schema-uid.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-schema-resource-value-is-an-xml-attribute-with-checkcharacters-on.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-server-built-user-task-keeps-the-template-nested-item-uids.md` | x | x | kept | RemintNestedItems runs in Create only at 1.6.6.109 |
| `platform/a-server-built-user-task-takes-the-existing-process-default.md` | x | x | kept | template values still copied verbatim on create |
| `platform/a-server-side-process-save-is-invisible-to-the-optimized-compile.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/a-terminate-event-hides-the-branches-queued-behind-it.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/activity-allowedresult-derives-from-conditional-flows.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/an-unbound-files-input-stops-the-process-from-starting.md` | - | x | kept | written by #1847 |
| `platform/an-unset-result-action-type-runs-as-save-to-files.md` | x | x | kept | an unset action is still described as a save with actionStored false |
| `platform/an-unset-single-file-fails-a-process-parameter-file-element.md` | - | x | kept | written by #1847 |
| `platform/attachment-storage-is-chosen-per-entity-by-the-designer.md` | x | x | kept | SysFile still refused |
| `platform/builder-process-parameters-carry-il2.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/connector-geometry-is-honoured-only-when-fully-orthogonal.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/deconverting-a-multi-instance-element-keeps-a-mapped-value.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/deconverting-with-live-readers-is-refused-by-validation.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/describe-itemproperties-reflect-the-worker-instance.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/designer-stamps-modified-in-schema-only-on-a-changed-value.md` | x | x | kept | template defaults still copied with their stamp |
| `platform/formula-depth-crash-is-reachable-from-the-designer-too.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/insync-is-vacuously-true-against-a-parameterless-callee.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/invoice-quotation-and-contract-printables-have-no-sysentityschema.md` | - | x | kept | written by #1847 |
| `platform/multi-instance-conversion-must-move-parameters-before-the-rebuild.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/object-file-processing-filters-on-the-file-object.md` | x | x | kept | run-time facts unchanged; the package's empty-filter refusal keeps its rule |
| `platform/parameter-displayvalue-lives-in-the-schema-resources.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/process-designer-is-entered-from-the-process-card-not-a-url.md` | x | x | kept | directory prefix docs/knowledge/platform/; a navigation fact |
| `platform/process-parameter-type-compatibility-is-kind-level.md` | x | - | kept | the Schema.cs edit was a comment |
| `platform/process-tracing-is-a-user-property-on-the-family-root.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/process-tracing-shows-a-callee-input-as-null.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/process-version-number-is-stamped-not-derived.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/processschemaparameter-datavaluetype-is-a-lazy-lookup.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/readdata-resultcount-is-never-assigned-outside-function-count-mode.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/sequence-flow-visualtype-is-written-as-a-literal.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/showinscheduler-checkbox-lives-on-the-base-user-task-page.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-card-reattaches-mappings-by-name.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-designer-card-hides-stale-state.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-host-is-invisible-on-both-draft-paths.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-insync-depends-on-the-schema-instance.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-runtime-binds-by-parameter-name.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-schemauid-setter-runs-the-parameter-sync.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-sync-flattens-a-multi-instance-element.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/subprocess-value-survives-only-under-two-stamps.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/the-designer-stores-a-lookup-constant-two-different-ways.md` | x | x | UPDATED | 'only in a change-data column mapping' false since #1841's designer form: the Process file ConnectedObjectId is stored as Script + [#Lookup#] (measured 2026-10-17) |
| `platform/the-first-builder-save-normalizes-a-designer-built-file-task.md` | x | x | kept | TypeAsLookup / DropSchemaRegistryReferencesOfWritten behave as recorded at 1.6.6.109 |
| `platform/the-platform-refuses-a-bad-flow-condition-at-save.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/the-platform-type-name-file-is-the-blob-type.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/the-process-file-palette-offers-only-the-object-variant.md` | x | x | kept | updated by #1847; accurate at 1.6.6.109 |
| `platform/the-process-file-source-is-three-user-task-schemas.md` | x | x | kept | a source change is still refused |
| `platform/the-process-parameter-file-source-only-saves.md` | - | x | kept | written by #1847; the binder still refuses useInProcess, requires saveTo and rewrites another stored action to 0 with a notice |
| `platform/the-synthesized-gateway-drops-one-unconditional-flow-and-runs-the-rest.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `platform/use-in-process-report-files-are-deleted-with-their-process.md` | - | x | kept | written by #1847 |
| `process/a-review-reads-the-diff-and-never-the-instructions.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `process/a-running-mcp-server-locks-the-debug-clio-binary.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
| `process/rebundle-refreshes-only-one-clio-build-output.md` | x | x | kept | touched only through a shared path (the archive, a tool description or a test file); its claims are not about what the PRs changed |
