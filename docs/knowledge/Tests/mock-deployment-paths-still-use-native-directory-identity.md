---
description: mocked deployment paths still use native Windows directory handles, so hardcoded IIS roots depend on the runner account and filesystem
applies-to:
  - clio.tests/Common/CreatioUninstallerTestFixture.cs
  - clio.tests/CreatioInstallerServiceTests.cs
  - clio/Common/IIS/DirectoryPathIdentity.cs
ticket: gh-1445
date: 2026-09-10
---

**What is true** — `DirectoryPathIdentity.Normalize` resolves the real existing ancestor on Windows with directory handles, even when a caller's file operations use `MockFileSystem`. Deployment fixtures therefore use unique descendants of the current user's normalized temp directory, with deployment files kept in the mock.

**Why it is this way** — production deployment/uninstall must resolve physical identity to defend against path aliases. That safety check deliberately does not trust a lexical mock path. Release 8.1.0.124 failed 41 tests on TS1-MRKT-WEB01 with unresolved physical identities under C:\inetpub; the same fixtures passed locally and in release 8.1.0.123 on TS1-CORE-DEV24. The logs establish resolution failure, not the specific native error or ACL responsible.

**What breaks if you ignore it** — an apparently isolated unit test fails before reaching its mocked port reservation or uninstall cleanup. Do not relax the production guard or skip the test; give the fixture an accessible real ancestor instead of relying on a machine-owned IIS directory.
