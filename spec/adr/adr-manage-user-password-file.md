# ADR: manage-user takes the new password from a file in clio's secrets folder

- **Status:** Accepted
- **Date:** 2026-11-14
- **Feature:** `manage-user-password-file`
- **Spec:** [spec-manage-user-password-file.md](../prd/spec-manage-user-password-file.md)

## Context

`manage-user` `create` and `password` send a new password to Creatio. clio never takes the password as an argument:
the caller names a `CLIO_ADMIN_PASSWORD_<SUFFIX>` variable of clio's process, and clio reads it
(`ManageUserCommand.ReadPassword`). The MCP tool runs in the host process for that reason, because workers strip
inherited secrets.

An MCP host that lets an AI agent create demo accounts must keep the password from the agent: the host makes the
password and the agent only names it. The environment variable does not allow that. The MCP server is a
long-lived process, and on macOS the environment of a process is readable by other processes of the same user
(`ps eww -o command=` showed a `node` child's variable on macOS with SIP on; only system binaries were hidden). The
agent's shell runs as that user. So the host cannot place the password there, and the agent cannot create accounts.

## Decision

1. **A second channel, `password-file`.** CLI `--password-file`, MCP `password-file`: the NAME of a file in
   `<clio home>/secrets/` (`ClioRuntimePaths.Home`, which honors `CLIO_HOME`). The host writes the file, mode 600,
   and gives the agent the name. clio reads it at call time. The password is never in the call, in the process
   environment, in the output, the logs, telemetry or an error. A host that runs clio for an agent keeps the agent's
   tools out of clio's home already, because clio's settings there hold the environment's sign-in; the secrets
   folder has the same protection.
2. **Fail closed, without echoing.** `ClioSecretFileReader` refuses:
   - a name that is not a plain file name (`[A-Za-z0-9_-][A-Za-z0-9._-]*`, at most 128 characters), without
     repeating it, because a malformed value may be a secret pasted by mistake;
   - a secrets folder or a file that is a link, so the read cannot be redirected;
   - a file other users can read (macOS and Linux; Windows relies on the per-user `%LOCALAPPDATA%` ACL, as
     `FileSecurityHardening` does);
   - a missing or empty file, a file over 4096 bytes, more than one line, or bytes that are not UTF-8.
   A trailing line break and a byte order mark are dropped; other whitespace is kept, as with `password-env`.
   Refusals name the reference and the folder, never the content.
3. **One channel per call.** Naming both `password-file` and `password-env` is refused. `password-env` behaves as
   before, message included.
4. **The MCP tool checks the name before resolving the environment**, so a malformed or ambiguous reference is
   refused without reaching a site.
5. **One mechanism for secrets.** `IClioSecretFileReader.Read(name, argumentName)` is the rule any later
   secret-taking argument uses, so a host learns one rule: write the secret to `<clio home>/secrets/<name>`, mode
   600, pass the name.
6. **The contract says which to offer.** The `manage-user` description and the `password-file` field say to
   prefer it, and that the name is one the host gives, never one the agent invents.

## Alternatives considered

- **Keep only the environment variable.** It stays, for hosts that already use it, but a host that hides the
  password from the agent cannot use it.
- **A path argument.** It would let a call point clio at any file the person can read; a name inside one folder
  cannot leave it.
- **The OS keychain.** Not portable across the platforms clio runs on, and an agent on macOS can often read the
  login keychain of the same user.

## Consequences

- A host detects the channel from `manage-user`'s input schema (`password-file` present) and otherwise falls
  back.
- The check and the read are not atomic. A swap in between needs write access to clio's home, which is the user's
  own and closed to the agent's tools in a host that uses this channel.
