---
description: cliogate cannot issue a Creatio auth cookie - it can append a raw cookie but not sign one, so forms login through CreatioAuthClient stays the only session-issuance path
applies-to:
  - cliogate/cliogate.csproj
  - cliogate/Files/cs/CreatioApiGateway.cs
  - clio/Common/BrowserSession/CreatioAuthClient.cs
ticket: ENG-91234
date: 2026-08-19
---

**What is true** — a recurring proposal is to add a cliogate endpoint that mints a browser session
without a password (for an environment whose password nobody knows, an OAuth-only environment, or to
switch to Supervisor). It was investigated and rejected. cliogate is an ordinary configuration
package: `cliogate.csproj` builds against `CreatioSDK`, `ATF.Repository` and `Newtonsoft.Json`, and
the cookie-signing entry points are host-side, in assemblies a configuration package cannot
reference. cliogate can append a raw cookie to a response but cannot sign one, and an unsigned cookie
is rejected. Forms login (`CreatioAuthClient`, `POST ServiceModel/AuthService.svc/Login`) remains the
only cookie-issuance path clio has; the ENG-91234 spike also found no OAuth token-to-cookie exchange
for clio's token on either host.

**Why it is this way** — the server does support passwordless issuance from a resolved user name, but
only from inside the host. That detail was established by reading `creatio-core`, which is not in
this repository, so treat it as inspected-elsewhere rather than verifiable here. Independently of it,
the package's reference surface is enough to settle the question.

**What breaks if you ignore it** — you spend the work writing the endpoint and it returns a cookie
the platform will not accept, so authentication fails in a way that looks like a clio bug. The second
reason it was rejected still stands even if a signing path were found: `CheckCanManageSolution` only
gates `CanManageSolution`, which is too coarse to authorize impersonating Supervisor, and the minted
cookie would land in the MCP/agent transcript.
