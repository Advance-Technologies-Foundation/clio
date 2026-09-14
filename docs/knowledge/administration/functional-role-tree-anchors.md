---
description: Native functional-role trees exclude arbitrary organizational parents and hide their functional children.
applies-to:
  - clio/Command/Administration/AdministrationService.cs
ticket: clio-968
date: 2026-09-11
---

**What is true** — Functional roles belong below another functional role or the built-in All employees
anchor (`a29a3ba5-4b0d-de11-9a51-005056c00008`). All external users
(`720b771c-e7a7-4f31-9cfb-52cd21c3739f`) is also an anchor when PortalUserManagementV2 is enabled.

**Why it is this way** — The deployed Creatio 10.1.585 `SysAdminUnitSectionV2.js` filters the functional
tree to type 6 plus those anchors (lines 1211–1221 and 2003–2009). Expansion reapplies that filter
and selects children by ParentRole (636–645). Creation uses the selected node as parent (1697–1702).
The native SaveRole service accepts more parent combinations than this tree can display.

**What breaks if you ignore it** — A functional role saved below an arbitrary division/team is
persisted but unreachable through the native functional tree. Apply the same invariant to creation
and parent updates; a successful SaveRole response alone does not establish native UI reachability.
