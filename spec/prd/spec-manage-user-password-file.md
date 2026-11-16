# manage-user: the new password from a file

- Problem: `manage-user` `create` and `password` take the new password only as the name of a
  `CLIO_ADMIN_PASSWORD_<SUFFIX>` variable of clio's own process. An MCP host runs clio as a long-lived process, and on
  macOS another process of the same user can read that process's environment (`ps eww`). An agent's shell runs as
  that user, so a host that keeps the password from the agent cannot put it there, and the agent cannot create
  accounts.
- Outcome: a second channel, `password-file`: the NAME of a file in clio's secrets folder
  (`<clio home>/secrets/<name>`), read at call time. The call carries a name, never a path or the secret; the
  password is never in the process environment, the output, the logs or an error.
- Contract: fail closed on a name that is not a plain file name (not repeated), a linked folder or file, a file
  other users can read (macOS/Linux), an empty file, a file over 4096 bytes, more than one line, or not UTF-8. A
  trailing line break is dropped. Naming both channels is refused. `password-env` is unchanged.
- Reuse: `IClioSecretFileReader` reads any secret by name from that folder, so a later secret-taking argument
  follows the same rule.
- Host detection: a clio that has the channel lists `password-file` in `manage-user`'s input schema
  (`get-tool-contract`); an older one does not, and the host falls back.
- Proof: unit tests for every refusal and for the command and the MCP tool; real stdio refusals in
  `clio.mcp.e2e` without an environment; a sandbox lifecycle case that creates the account from a file.
- Design: [adr-manage-user-password-file.md](../adr/adr-manage-user-password-file.md).
