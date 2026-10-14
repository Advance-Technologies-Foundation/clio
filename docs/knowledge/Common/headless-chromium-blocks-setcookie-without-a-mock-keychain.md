---
description: a headless Chromium started for page-smoke answered Network.setCookie never (not with an error) when HOME pointed away from the user's keychain - --use-mock-keychain and --password-store=basic fix it
applies-to:
  - clio/Common/BrowserSession/HeadlessBrowserLauncher.cs
ticket: ENG-103032
date: 2026-10-13
---

**What is true** — Chromium encrypts its cookie store with a key it reads from the OS keychain (macOS
Keychain, a Linux keyring). The first `Network.setCookie` makes it fetch that key. Measured on macOS
2026-10-13: run under the MCP e2e harness, which points `HOME` at a temporary directory, the browser answered
`Runtime.enable`, `Log.enable`, `Page.enable` and `Network.enable` at once and then never answered
`Network.setCookie` - no error, no event, just silence until clio's 10 s command timeout. The same calls
from a test process with the user's own `HOME` answered in milliseconds. Starting the browser with
`--use-mock-keychain` (macOS) and `--password-store=basic` (Linux) makes it answer in both cases.

**Why it is this way** — the keychain lookup blocks the cookie store, and Chromium has no timeout for it; with
the user's real keychain it can also raise a "Chrome Safe Storage" access prompt on the desktop. page-smoke's
profile is a throw-away directory deleted after the call, so a mock key protects nothing that matters.

**What breaks if you ignore it** — dropping either flag from `HeadlessBrowserLauncher.BuildArguments` makes
page-smoke fail with "The browser did not answer Network.setCookie within 10 s" on any agent whose `HOME`
or keychain is not the interactive user's (CI agents, the e2e harness, a service account), and can pop a
keychain prompt on a developer's machine. Puppeteer and Playwright pass the same flags by default for the
same reason.
