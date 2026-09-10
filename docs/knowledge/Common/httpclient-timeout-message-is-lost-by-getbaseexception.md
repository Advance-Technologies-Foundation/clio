---
description: GetBaseException unwraps an HttpClient timeout to "A task was canceled.", so classifying a transport failure by matching words in the message never matches a real timeout
applies-to:
  - clio/Command/EnvironmentRuntimeDetectionService.cs
date: 2026-09-10
---

**What is true** — when `HttpClient` aborts a request on its own `Timeout`, the exception it throws is a
`TaskCanceledException` whose own `Message` names the timeout ("The request was canceled due to the configured
HttpClient.Timeout of 10 seconds elapsing"), but whose `InnerException` is a plain `TimeoutException` /
`OperationCanceledException`. `exception.GetBaseException().Message` therefore yields `A task was canceled.` —
the wording that identifies the timeout is on the outer exception and is gone by the time it is logged.

The same holds for a Windows socket timeout: the useful text is "A connection attempt failed because the
connected party did not properly respond after a period of time", which contains neither "timeout" nor
"timed out".

**Why it is this way** — `GetBaseException` is used to strip the wrapper layers that `HttpClient` and
`CreatioClient` add, so the surfaced message names the real cause. For a timeout that heuristic works against
itself: the cause is the wrapper.

**What breaks if you ignore it** — any code that decides "was this a connectivity failure?" by searching the
message for `timeout` / `timed out` silently answers no on every real timeout. In
`EnvironmentRuntimeDetectionService` that meant a host that accepted the connection and then never answered was
reported as an undecidable runtime, telling the operator to pass `--IsNetCore` for a site that was not
responding. Classify by whether an HTTP status code came back (`ProbeAttempt.StatusCode is null` means no HTTP
response at all) rather than by message text; the text is for the human reading the diagnostic, not for the
decision.
