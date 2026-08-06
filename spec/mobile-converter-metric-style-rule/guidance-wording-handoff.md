# Handoff: guidance wording for clio-knowledge (ENG-94230)

PR #1010 changed the `freedom-page-web-to-mobile-conversion` article, but ENG-94188's
"Externalize guidance delivery mechanics" (#927) removed every guidance article from this
repository — they are now published from
[clio-knowledge](https://github.com/Advance-Technologies-Foundation/clio-knowledge), one Markdown
file per article under `guidance/`, indexed by `bundle-source.json`.

The wording below therefore CANNOT land here. It needs its own pull request in clio-knowledge,
with a `libraryVersion` + `sequence` bump, and `curated-knowledge-names.json` re-pinned if the
generation changes. Until that lands, the published article still describes the pre-ENG-94230
behaviour: it names a fixed metric style instead of pointing at `guide.normalizations`.

## Section list entry — replaces the old `spacingNormalization` bullet

```text
			  - normalizations — ONE SECTION PER STANDARD the conversion RULES declare, keyed by the rule's
			    reportGroup (today "spacing" and "metricStyle"; the set is OPEN — the rules file is resolved at
			    runtime, so a build can meet a section it has never heard of). Each section carries:
			      • note — what the standard did, in the rules' own wording;
			      • normalized[] — {name, type, properties}: the elements normalized and the EXACT dotted paths
			        written on each. This is the authoritative list — never assume a fixed set of properties,
			        and never assume a value from this article;
			      • skipped[] — {name, type, properties, reason}: elements the standard could NOT be applied to,
			        because a branch is a whole-value binding the converter refuses to overwrite. These keep
			        the WEB values and may need a manual pass in the designer — mention them separately;
			      • ruleNotes[] — the rules' own rationale, when they carry one.
			    Every normalization is SILENT — never a gate question: state each section as ONE aggregated line
			    in the plan and the final report, and never restore the web values. Null when nothing was
			    normalized. spacingNormalization is kept as a BACK-COMPAT ALIAS of the "spacing" section; new
			    callers should read normalizations.
```

## Hard rule — replaces "SPACING IS NORMALIZED, NOT CONVERTED"

```text
			- NORMALIZATION IS NOT CONVERSION: the converter stamps mobile design standards onto the elements it
			  inserts, and the web page's own values for those properties are deliberately IGNORED (discarded,
			  not translated) — container spacing and metric style today, whatever the conversion rules declare
			  tomorrow. It is ALREADY baked into elementMap[].mobileValues; there is nothing separate to apply.
			  WHICH properties were written is reported per element in guide.normalizations.<group>.normalized[]
			  — read it there. Merge twins the mobile template provides are untouched, and a branch that is a
			  whole-value binding is NEVER overwritten (it appears under .skipped[] and keeps the web value).
			  A merge preserves the sibling subtrees of what it stamps — for a metric that means config.data
			  (the aggregation subtree, without which the widget renders nothing) and config.title survive, so
			  never reconstruct config from the normalized keys alone. Like tabAreaLayers this is NOT a
			  proposal — SILENT, never a gate question: state each standard as ONE aggregated line in the plan
			  and the final report, mention skipped elements separately, and do NOT treat the difference from
			  the web page as a defect. Each section's own note carries the rest.
```
