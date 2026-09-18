# login

Signs in to an SSO-enabled Creatio environment using OAuth authorization code and PKCE. The configured client must be public, allow `authorization_code` and `refresh_token`, require PKCE S256, and contain the redirect URI registered for clio.

Aliases: `signin`

```bash
clio login -e work
clio login -e work --no-browser
```

`--no-browser` prints the authorization URL and reads the full redirect URL from stdin. `--timeout` defaults to 120000 ms; `--force` starts a new sign-in even when a cached token exists. Tokens are never printed or stored in `appsettings.json`.
