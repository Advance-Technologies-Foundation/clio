# enroll-sequence-participants

Enroll an explicit selection of 1–100 unique contacts through Creatio's native sequence service.

```sh
clio enroll-sequence-participants -e dev --sequence-id <uuid> --contact-ids <contact-uuid>,<contact-uuid>
```

The environment must provide the native sequence bulk-enrollment service and grant the caller its normal permissions. The command builds an exact Contact-ID filter, submits once, and reads participant statuses with DataService. Native eligibility, duplicate detection, capacity, initial stages and activity creation remain authoritative. It does not activate the sequence. Enrollment into an already active sequence may create activities or trigger configured outreach; review the definition first.

The result separates `completion` (`completed` or `uncertain`), `platform-success`, `added-count`, `failed-count`, sanitized `errors`, and a bounded `readback`. `success` requires a successful platform response and complete readback. `completed` describes receiving a valid response, not universal enrollment or activation. Added participants may be Pending. Readback may include earlier enrollments and is a snapshot, not attribution to this call.

The write has a 30-second HTTP timeout and no transport or authentication replay. A lost, malformed or oversized response is `uncertain`; inspect records and activities before resubmission. A failed readback never causes a second write. Reads have a ten-second timeout. Readback contains at most 100 participants and reports truncation; raw responses above 200,000 UTF-8 bytes are refused. At most 20 native error messages plus the service error are returned with bounded, redacted text.

MCP: use `clio-run` with `command: "enroll-sequence-participants"` and arguments `environment-name`, `sequence-id` and `contact-ids` (a UUID array). The tool is destructive and non-idempotent; discover its exact schema with `get-tool-contract`.

The optional diagnostic object adds operation/entity context, write-attempt and transport boundaries, side-effect certainty, and safe retry advice. A native failure after submission does not establish rollback. No HTTP status or offending field is inferred from prose.
