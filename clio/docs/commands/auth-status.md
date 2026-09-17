# auth-status

Reports the configured authentication flow, whether a cached access token is usable, and its expiry. It never opens a browser or refreshes interactively and never prints token material.

Alias: `whoami`

```bash
clio auth-status -e work
```

Exit code `0` means a usable session is cached; `1` means sign-in is required.
