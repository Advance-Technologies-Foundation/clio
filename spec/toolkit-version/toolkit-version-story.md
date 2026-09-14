# Show toolkit versions in clio ver

Issue: #1500. Status: review.

As a toolkit user, I can run `clio ver` after `clio update-toolkit` and see the
version installed for each coding agent alongside existing component versions.

Acceptance and validation:

- [x] Default and `--all` output include per-agent toolkit information.
- [x] Local metadata is authoritative; absence and unreadability are explicit.
- [x] Component selectors and existing output remain supported.
- [x] Unit coverage and a real CLI probe pass.
- [x] Documentation and MCP/Ring compatibility reviews are complete.
- [ ] Required reviews and delivery gates pass.
