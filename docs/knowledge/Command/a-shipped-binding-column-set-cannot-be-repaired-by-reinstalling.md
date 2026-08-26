---
description: non-key data-binding columns ship with IsForceUpdate false and an unchanged package version installs no data at all, so a binding that shipped the wrong column set cannot be corrected by reinstalling on an environment that already holds the row
applies-to:
  - clio/Command/DataBindingCommand.cs
  - clio/Command/DataBindingDbCommand.cs
  - spec/data-binding/data-binding.md
ticket: ENG-88474
date: 2026-08-19
---

**What is true** — `clio/help/en/read-data-binding-db.txt` already states the projection rule: a
binding ships only the columns it was created with and install supplies no default for the rest. What
it does not say is that the mistake is one-way. Every non-key column is written with
`"IsForceUpdate": false` (spec/data-binding/data-binding.md:75, "Always use false"), and installing a
package whose version has not changed applies no data rows at all. Correcting the binding and
reinstalling therefore changes nothing on an environment that already received the broken row.

**Why it is this way** — `IsForceUpdate: false` is the platform's "do not overwrite what the customer
edited" contract, and the installer skips data for an unchanged package version as an optimisation.
Both are correct for their purpose and neither knows that the previous install shipped an incomplete
projection.

**What breaks if you ignore it** — a wrong column set is discovered only on the second environment,
where the row arrives with empty values (an empty `SysApplicationClientType` / `Type` / `LoaderId`
makes a transferred workplace fail to render), and every attempt to repair it by fixing the binding
and reinstalling reports success while applying nothing. Prove a binding fix on a freshly provisioned
target where the row does not exist yet; on an environment that already has it, the only remaining
route is a live write.
