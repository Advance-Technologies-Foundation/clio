# manage-user reads the new password from a file

Status: review
Spec: [spec-manage-user-password-file.md](../prd/spec-manage-user-password-file.md)
Design: [adr-manage-user-password-file.md](../adr/adr-manage-user-password-file.md)
Test plan: [tp-manage-user-password-file.md](../test-plans/tp-manage-user-password-file.md)

As an MCP host that lets an agent create accounts, I write the new password to an owner-only file in clio's secrets
folder and give the agent its name, so the agent creates the account without ever seeing the password and the
password is never in clio's process environment.

Acceptance:
- `manage-user` `create` and `password` accept `password-file: <name>` (CLI `--password-file`).
- Refused: an invalid name (not echoed), a link, a file others can read, a missing, empty, oversized, multi-line or
  non-UTF-8 file, and both channels at once.
- The password never appears in output, logs or errors; a trailing line break is dropped.
- `password-env` is unchanged.
- The MCP tool refuses a malformed or ambiguous reference before resolving the environment.
