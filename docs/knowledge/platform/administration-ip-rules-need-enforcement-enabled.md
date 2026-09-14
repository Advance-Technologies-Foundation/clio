---
description: SysAdminUnitIPRange rows do not restrict login unless native IP enforcement is enabled
applies-to:
  - clio/Command/Administration/AdministrationService.IpRanges.cs
ticket: clio-968
date: 2026-09-11
---

**What is true** — Native UserConnectionFactory checks IP ranges only when its login path requests
the check and either AppConnection.UseIPRestriction or the UseRestrictedIP system setting is true.
The setting is false in the local Creatio 10.1.585 template. Creating a valid range succeeds even
when login ignores it. IPVersion is a UI-only field; native persisted ranges contain BeginIP/EndIP.

**Why it is this way** — Storing rules and enabling environment-wide login enforcement are separate
platform controls. The administration tool changes the requested rule, not the global switch.
On .NET 8/PostgreSQL, a fresh probe login succeeded with a restrictive range while the setting was
false, failed after enabling it, and succeeded after cleanup. The original setting was restored.

**What breaks if you ignore it** — A successful CRUD/readback test can be reported as access-control
proof while the same credentials still authenticate from a forbidden address. Verify a fresh login
from the intended address, and do not enable the global setting outside the user's authorized scope.
