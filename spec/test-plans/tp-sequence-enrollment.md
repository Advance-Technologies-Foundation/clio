# Native sequence enrollment validation

Unit: invalid/empty/oversized selections cause no write; exact filter matches only requested contacts; routes work on .NET Framework and Core; write uses no-replay transport with one attempt; successful/partial/denied/malformed/timeout responses preserve completion certainty; status readback failure never resubmits enrollment; errors are sanitized and bounded.

MCP: discover destructive, non-idempotent tool; reject malformed arguments; route explicit environment to the real command. Live isolated lab: synthetic contacts, draft/inactive and active sequence, duplicate/ineligible contacts, active cap saturation, native activity ownership and status readback. No automatic email step is used.
