---
description: a schema resource value is written into an XML ATTRIBUTE by XmlWriter.WriteAttributeString and nothing in the platform sets CheckCharacters, so the framework default of true applies - an XML-invalid character in a caption throws on somebody else's later package export, naming neither the caption nor the flow
applies-to:
  - clio/Command/ProcessModel/FlowLabelExpectation.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio.tests/Common/BundledProcessBuilderPackageTests.cs
ticket: ENG-91853
date: 2026-09-09
---

**What is true** — a localizable value on a schema (an element caption, a flow caption) is extracted
into the schema resource file and written there as an XML **attribute value**:
`Terrasoft.Common/ResourceItem.cs:1036` calls `writer.WriteAttributeString(ValueAttribute, StringValue)`.

Nothing in the platform sets `XmlWriterSettings.CheckCharacters` — grep `CheckCharacters` across
`TSBpm/Src/Lib` returns nothing — so the framework default of **`true`** applies, and the writer
throws on any character XML 1.0 forbids in character data.

The forbidden set is wider than "control characters", which is the trap:

- the C0 controls except tab, LF and CR;
- **`U+FFFE` and `U+FFFF`**, which are `Cn` non-characters and are NOT control characters, so
  `char.IsControl` is false for both;
- **a LONE surrogate half.** A surrogate *pair* is one legal astral character and must survive, so a
  filter for this cannot be a per-character predicate — it needs a pairwise scan.

**Why it is this way** — the resource file is ordinary XML and the writer is the framework's. Nobody
chose to validate captions; validation is simply what `XmlWriter` does by default, at the moment of
serialization rather than at the moment of assignment.

**What breaks if you ignore it — and the failure is displaced, which is the whole problem.** Writing
such a caption SUCCEEDS. The save succeeds. The designer draws the label. The throw arrives later, on
somebody else's `pull-pkg`, package export or FSD save, as an XML serialization error naming neither
the caption, nor the flow, nor the person who wrote it. Nothing connects the two events.

So a caption must be filtered at the point it is stored, and `CrtProcessBuilder`'s
`ProcessGraphBuilder.StripXmlInvalid` does that. Two consequences worth knowing before touching it:

- **A label that filters to EMPTY must be refused, not stored.** An empty caption is how a caption is
  CLEARED, so storing the filtered result would delete an existing designer-authored label while
  reporting that a new one was set.
- **The rule is hand-mirrored in clio** as `FlowLabelExpectation.IsUnstorable`, because clio predicts
  what the server will store in order to compare a read-back. If the two halves disagree, clio
  reports "the label came back DIFFERENT" on a write that landed exactly as sent — and the two quoted
  strings in that message can look identical, because the character at fault is invisible. What
  detects the divergence is one archive-content probe in
  `clio.tests/Common/BundledProcessBuilderPackageTests.cs`; the SHA pin cannot, because it detects a
  CHANGED archive rather than a stale one, and says so in its own remark.

**Do not maintain this as a list of code points.** The first version of the filter tested
`char.IsControl` alone and shipped; the `U+FFFE` gap was found by review, not by a suite full of
hand-picked characters. The package test `StripXmlInvalid_KeepsOnlyWhatAnXmlAttributeCanHold` drives
every BMP code point through the real write funnel and then asks `XmlWriter` itself — that is the only
construct here that can catch the NEXT gap without somebody thinking of it first.
