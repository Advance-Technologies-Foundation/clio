# auth-status

Reports the configured authentication flow, whether a session is cached, and the access-token expiry. It never opens a browser or refreshes interactively and never prints token material.

Alias: `whoami`

```bash
clio auth-status -e work
```

Exit code `0` means a session is cached (an expired access token is renewed by the next command without a browser); `1` means sign-in is required.
