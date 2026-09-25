# logout

Revokes the cached OAuth refresh token and deletes the local token file. Local cleanup is performed even if server-side revocation fails; the command is idempotent.

Aliases: `signout`

```bash
clio logout -e work
```
