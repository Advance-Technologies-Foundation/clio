---
description: PackageDataBinder.BindFeatureOffState writes the Feature_<code> definition folder before the AdminUnitFeatureState row folder on purpose - a throw on the second SaveBinding must not leave a row shipped without its definition
applies-to:
  - clio/Command/PackageDataBinder.cs
ticket: ENG-93848
date: 2026-08-19
---

**What is true** — in `BindFeatureOffState` the `SaveBinding` for the `Feature` definition folder runs
before the `SaveBinding` for the `AdminUnitFeatureState` folder. The order is load-bearing and there is
no comment saying so. The same rule holds for the caller's pair in
`SetBackgroundImageCommand`: the `SysImage` row is bound before the `CrtBackgroundConfig` value that
names it, and the config is withdrawn (`RemoveSysSettingsValue`) when the image is not bound.

**Why it is this way** — `SaveBinding` is one platform round trip per folder, so a group of related
folders is never atomic. Whichever folder is written second is the one that can be missing after a
throw. A definition without its state row is harmless — it resolves on its own and adds nothing. A
state row without its definition references a `Guid` the package does not carry.

**What breaks if you ignore it** — swap the two calls and a failure on the second write (an incomplete
projection, a rejected `SaveSchema`, a dropped connection) leaves the package shipping a row whose
definition it does not ship. The build succeeds, the package installs on the target, and the row is
inserted pointing at a `FeatureId` that resolves to nothing. Whenever a delivery groups a referencing
row with the row it references, write the referenced one first and drop it last.
