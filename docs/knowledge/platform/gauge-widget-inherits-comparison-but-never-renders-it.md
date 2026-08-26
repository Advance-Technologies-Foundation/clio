---
description: crt.GaugeWidget inherits config.comparison from crt.IndicatorWidget and the shared generator even builds its attributes, but the gauge template draws no trend badge - the config is inert
applies-to:
  - clio/Command/SchemaValidationService.cs
ticket: ENG-95576
date: 2026-08-26
---

**What is true** — `GaugeWidgetConfig` extends `IndicatorWidgetConfig`, so `config.comparison` is a
type-valid field on a gauge, and the shared `IndicatorWidgetDataProvidingMetadataGenerator` will
happily build the `_Difference` / `_Config` view-model attributes for it. The gauge nevertheless
renders **no** trend badge: its template contains only the gauge chart, and its properties panel has
no comparison section. The badge markup lives in the indicator widget's own template.

**Why it is this way** — the gauge was built by extending the metric tile to reuse its data
providing, aggregation and theming, and the config type came along with the base class. Only the
presentation was replaced, so the inherited field has no renderer behind it.

**What breaks if you ignore it** — a payload with `config.comparison` saves and renders cleanly and
simply does nothing, which is the worst shape of failure for an agent: it reports success, and the
requirement ("show the value with a trend versus last period") is quietly unmet. Treat a
comparison request on a gauge as a signal to use `crt.IndicatorWidget` instead. Supporting it on the
gauge is a creatio-ui change — template plus properties panel — not a clio one, and is out of scope
for ENG-95576. `ValidateGaugeWidgetConfig` reports it as a warning rather than an error because the
page is not broken, only pointless.
