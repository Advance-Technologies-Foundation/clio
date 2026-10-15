---
description: Creatio saves a reference-schema change on an inherited lookup only for a virtual parent and a non-replacing schema, drops it silently otherwise, and never checks that the new target belongs to the old reference's hierarchy
applies-to:
  - clio/Command/EntitySchemaDesigner/InheritedLookupOverride.cs
ticket: ENG-102999
date: 2026-10-13
---

**What is true** — the Object designer redirects an inherited lookup (for example `Folder` of a
`BaseItemInFolder` child -> `ContactFolder`) by keeping the column in `inheritedColumns` and changing only
its `referenceSchema` in the same `EntitySchemaDesignerService.SaveSchema`. The core mapper applies that
change only when the design schema's `parentSchema.isVirtual` is true and `extendParent` is false; in every
other case it drops the change and `SaveSchema` still answers success. The server does not check that the
new target inherits from the column's reference in the parent: `Folder` -> `Contact` saves. Sending the
same name in both `columns` and `inheritedColumns` fails in `EntitySchemaValidator.ValidateColumnsHaveUniqueNames`.
A `BaseItemInFolder` child saved while `Folder` still references `BaseFolder` is refused with
`The "Folder" column cannot refer to a virtual object`, so such a child can be created only with the override.

**Why it is this way** — both rules live on the server, in the core mapper
`EntitySchemaDesignSchemaDtoToDesignItemMapper`, outside clio, and the service validates neither
precondition back to the caller; clio is the only place that can refuse the request before saving.

**What breaks if you ignore it** — without the pre-save checks in `InheritedLookupOverride` a create or
modify reports success while the column still references the old schema, and an unrelated target saves a
folder pair whose folder lookup points at a non-folder entity. Without the read-back
(`VerifyPersisted`) a dropped change is indistinguishable from a saved one.
