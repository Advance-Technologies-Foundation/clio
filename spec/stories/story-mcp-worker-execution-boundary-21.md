# Story 21: truncating the worker's standard error can un-redact a secret

Found 2026-08-18 while designing the tests for story 17's truncation signal, and verified against the
redactor's own patterns. This is a production hole, not a test artifact.

## The defect

`WorkerStandardErrorDrain` keeps the LAST N characters of the child's standard error, trimming from
the front at an arbitrary offset — wherever the buffer happens to be when the next chunk arrives. The
cut therefore lands in the middle of a line often enough to matter.

`SensitiveErrorTextRedactor.CredentialPairRegex` matches a KEY followed by a separator and a value:

```
\b(password|pwd|pass|secret|token|api[_-]?key|client[_-]?secret|access[_-]?key|connection ?string
  |data ?source|server|host|hostname|initial ?catalog|database|uid|user ?id|authorization|auth
  |bearer|cookie)\b\s*[=:]\s*[^\s,;"']+
```

The key is required. So a tail that begins mid-token — `word=SUPER-SECRET-VALUE`, the `pass` of
`password=` having been trimmed away — no longer matches any alternative, and the value is copied
verbatim into `worker-stderr` on the failure envelope, which goes to the client.

The self-identifying shapes still hold: `JwtRegex` matches the `eyJ` header prefix wherever it
appears, `BearerTokenRegex` matches `Bearer <token>` without needing a preceding key, and the URI and
host-port patterns are shape-based. It is exactly the ordinary `key=value` credential — the most
common shape in a stack trace or a connection-string dump — that the truncation defeats.

This sits directly against R-7 in the credential threat model, and against TC-U-505, whose secret
marker survives redaction only because the test's fixture happens not to manufacture the orphaned
case. That is test hygiene, and it should not be mistaken for coverage.

## Acceptance criteria

- AC-01 A truncated tail cannot begin part-way through a line. Dropping the first partial line of a
  trimmed tail is the obvious remedy and costs one line of a log the reader was never going to be able
  to interpret anyway — but the choice is a design call, and whatever is chosen must be stated with
  its reasoning.
- AC-02 A test that fails against the current code: a tail trimmed so that a `password=` key is cut in
  half must not leak its value. Construct the orphan deliberately; a fixture that pads its chunks so
  the cut lands in filler cannot catch this, which is precisely how it went unnoticed.
- AC-03 The redaction is applied to what the caller actually receives, asserted end to end through the
  failure envelope rather than on the redactor in isolation.
- AC-04 The threat model records this under T-6/R-7 as a defeat of the redaction by an upstream
  transformation — a class of failure worth naming, because any other bounded copy of untrusted text
  in this system has the same exposure.

## Note on scope

Fixing this in the drain covers the worker's standard error. Whether any other bounded or truncated
copy of untrusted text reaches a caller through the same redactor is not answered here, and AC-04
exists so the question is at least asked.
