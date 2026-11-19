---
description: creatio.client 2.1.0 signs in again and re-sends any request after a 401, a sign-in redirect or (cookie sessions) a 403 while the sign-in changed, showing clio only the second answer and offering no per-request opt-out; clio's non-replayable writes do not stop it, so odata-create sends its own GUID Id and reads an unknown row back by it
applies-to:
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/ReauthExecutor.cs
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
date: 2026-11-19
---

**What is true.** creatio.client 2.1.0, the HTTP client under `CreatioClientAdapter`, sends every request through
one loop (`CreatioClient.SendAsync`) that allows one session recovery. This covers GET, POST, PATCH, DELETE, PUT,
image uploads and chunked uploads.

When its auth handler reports an expired session, the client signs in again and sends the same request again. It
disposes the first answer, so clio sees only the second. The handler reports an expired session:
- after any 401, for a cookie session (login and password) and for OAuth client credentials;
- after a redirect to `/Login/`, for a cookie session;
- after a 403 on a cookie session, when the sign-in generation changed while the request was in flight. clio runs
  requests side by side inside one call, for example compile's build request beside its history polls.

A static bearer token (`AccessToken` / `ExternalAccessToken`) is never re-sent: no refresh is set, and the session
check is skipped.

Every send method builds a fresh recovery state, so there is no per-request opt-out. `replayAllowed: false`,
`ExecuteNonReplayablePostRequest`, `SiteCall.NonReplayablePost`, `Patch` and `Delete` turn off only clio's own
`ReauthExecutor` replay (see `reauth-never-replays-a-write.md`). The client's re-send below it still happens, and a
replayable POST can go out up to four times.

**When the first send can have been applied.** Usually it was not: an expired session is refused before the
handler runs. But it can have been in these cases:
- a handler works and then redirects. ASP.NET's ScriptModule turns any redirect during a REST call into the 401
  "Authentication failed." envelope;
- a gateway that inspects responses answers 403 after the backend ran, while another request of the same client
  signs in again.

This has not been seen on Creatio.

**What clio does about it.** `odata-create` sends its own `Id` (a new GUID) with a row that has none, but only
when `$metadata` declares the entity's `Id` as `Edm.Guid`, because some entities key on a number or a text. A
second send of that row is then refused as a duplicate key. A row whose result is then unknown is read back by
that `Id`:
- found means the row was created: no other request carries that `Id`;
- not found keeps the result unknown and adds `sent-id`. A re-send with that `Id` cannot create the record twice.

An `Id` the caller chose is not read back, because it may have existed before the call.

The other writes stay exposed:
- a restart can run twice;
- BuildProcess can create a second process;
- odata-update can run update-time processes twice.

The real fix is a per-request opt-out in creatio.client. That is a change to creatio.client and a version bump in
clio.

**What breaks if you ignore it.** Reading a write that clio "never replays" as sent once. Two rows from one
odata-create call, or two restarts, would then look impossible and go undiagnosed.
