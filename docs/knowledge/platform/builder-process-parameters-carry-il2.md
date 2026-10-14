---
description: process parameters the builder creates store IL2 (ContainerUId) = the process UId and the designer's own carry none; same meta path, no runtime effect, so a metadata comparison must normalise it
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95984
date: 2026-10-14
---

**What is true** — a process parameter created by CrtProcessBuilder (and the `File` item of a
FileCollection it creates) stores `IL2` (`ContainerUId`) equal to the process UId; the shipped
designer-made captures (`FileParameterProcess`, `MarkProcessesToCancel`) store no `IL2` on
process-level parameters. Measured on the .NET Framework stand on 1.6.6.97 and 1.6.6.98
(2026-10-14) by comparing exported builder metadata with those captures. A designer save keeps the builder's
`IL2`: it is not among the differences an unchanged designer save makes. The meta path that addresses the parameter is the same either way.

**Why it is this way** — the builder sets `ContainerUId = schema.UId` explicitly when it constructs a
parameter; the designer's client never writes the key for a process-level parameter.

**What breaks if you ignore it** — nothing at run time. The trap is in verification: a byte comparison
of builder metadata against a designer capture reports `IL2` as a difference on every parameter and
hides the real ones. Normalise it (the ENG-95984 comparison treated a process-level `IL2` equal to the
process UId as absent) instead of "fixing" the builder, which would change the stored shape of every process it has ever created.
