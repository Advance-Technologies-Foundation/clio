---
description: a targetProcessParameter mapping is the parameter's one stored value source, re-evaluated on every read - NOT a static default evaluated once at start, and NOT an assignment made when a branch runs; the ENG-103081 report's "static default" reading is false
applies-to:
  - clio/Command/ProcessModel/ProcessParameterBindingWarnings.cs
ticket: ENG-103081
date: 2026-10-13
---

**What is true** — a `mappings[]` / `addMapping` entry with `targetProcessParameter` and an element source is
stored as the PARAMETER's own value (`source: Script`, a `[#...Element...Parameter...#]` meta path), and the
runtime evaluates it whenever the parameter is read. Measured on sandbox `sae_m_seeenu_16140743_1009`
(2026-10-13) with run-process result parameters and trigger-smoke: a Formula placed BEFORE the bound Read data
read the parameter empty and one placed AFTER it read the column value (`early:` vs `late:Administrator_000`);
a count and a column binding both returned their element's value at the end of the run; two mappings onto one
parameter kept only the LAST listed (`PDup` saved as `second`); in a two-signal-start process with each branch's
read mapped into one shared parameter, the run started by the first signal read it EMPTY, because the surviving
binding points at the other branch's element, which never ran - while the same process with only that branch's
mapping, or with a Formula `resultProcessParameter` on each branch, produced the contact's name. A Formula
assignment onto a still-bound parameter wins over the binding.

**Why it is this way** — the platform has one value source per process parameter, and a mapping writes that
source; there is no per-branch or per-element "assign on completion" slot in a process parameter. The only
flow-time write is an element that assigns, such as the Formula element.

**What breaks if you ignore it** — the ENG-103081 report concluded, from process tracing, that the mapping is a
"static default evaluated once before any read runs". Taken at its word, that reading makes every
element-to-process-parameter mapping look broken, and a warning built on it would fire on the supported
"expose an element output as a process output" pattern of every single-start process - a false warning on the
commonest shape, teaching agents to ignore the family. That is why `ProcessParameterBindingWarnings` warns only
on the two shapes that are wrong whenever they occur - a parameter bound twice, and a binding to an element some
start of a multi-start process does not reach - and why the tool contracts say "BINDING, read live, not a
flow-time assignment" rather than "static default".
