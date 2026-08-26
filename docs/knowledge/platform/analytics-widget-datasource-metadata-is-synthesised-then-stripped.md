---
description: crt.ChartWidget crt.GaugeWidget crt.IndicatorWidget and the pipeline/waterfall widgets get their modelConfig dataSources and viewModelConfig attributes generated at metadata init and stripped again on save - author only viewConfigDiff
applies-to:
  - clio/Command/SchemaValidationService.cs
  - clio/Command/McpServer/Tools/GaugeWidgetValidation.cs
  - clio/Command/McpServer/Tools/ChartWidgetValidation.cs
ticket: ENG-95576
date: 2026-08-26
---

**What is true** — an analytics widget on a Freedom UI page is a **one-section** edit: the
`viewConfigDiff` insert carries the whole widget. Its `modelConfig.dataSources` entry (named
`<config.data.providing.attribute>DS`) and its `viewModelConfig.attributes` entries are *generated*
from `config.data.providing` when the page's metadata is initialised, and *removed again* when the
page is saved. The widget types this applies to are the ones registered in the platform's widget
data-providing preprocessor: `crt.ChartWidget`, `crt.GaugeWidget`, `crt.IndicatorWidget`,
`crt.PipelineMovementWidget` and the two waterfall widgets.

The observable proof is a designer-authored page read back with `get-page`: the merged body shows
`viewModelConfig.attributes: {}` and a `modelConfig` with no `dataSources` at all, even though the
widget renders live data.

**Why it is this way** — the platform pairs a metadata **pre**processor (generate on load) with a
**post**processor (compress on save), so the persisted schema stays minimal and the generated shape
can change between releases without rewriting stored pages. `crt.GaugeWidget` is mapped to the
indicator widget's generator, which is why the two have identical `data.providing` mechanics.

**What breaks if you ignore it** — hand-authoring the datasource is not merely redundant, it is
harmful: the generator produces its own entry under the `<attribute>DS` key, so an
agent-written one either collides with it or is silently discarded, and the hand-written
view-model attributes are regenerated regardless. Documentation that instructs an agent to write
all three sections (as the shipped `gauge-widget.component.md` did before ENG-95576) sends every
consumer down that path. Before adding a validator or a guide for the *next* analytics widget,
check the preprocessor's registration list first — membership there means "viewConfigDiff only".
