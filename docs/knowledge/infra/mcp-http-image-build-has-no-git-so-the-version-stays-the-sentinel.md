---
description: deploy/mcp-http/Dockerfile copies only csproj/props into the build stage, so SetVersionFromGitTag cannot run and the published clio-app reports the 0.0.0.0 sentinel version - the image tag comes from git describe on the host in the Jenkinsfile
applies-to:
  - deploy/mcp-http/Dockerfile
  - deploy/mcp-http/Jenkinsfile
  - clio/clio.csproj
ticket: ENG-92868
date: 2026-08-19
---

**What is true** — clio's assembly version is not written in `clio/clio.csproj`. The file defaults
`AssemblyVersion` to the sentinel `0.0.0.0` and a `SetVersionFromGitTag` target replaces it from the
latest git tag, unless the caller passes `/p:Version` or `/p:AssemblyVersion` explicitly. The
container build copies `Directory.Packages.props`, `clio.slnx` and the project/sources into the SDK
stage but no `.git` directory, and passes no version property, so neither source of a real version is
available: the published `clio-app.dll` carries `0.0.0.0`. The image itself is still labelled
correctly because `deploy/mcp-http/Jenkinsfile` computes `git describe --tags --always` on the host
and uses that for the tag.

**Why it is this way** — copying `.git` into the build context would invalidate the Docker layer cache
on every commit and bloat the context; taking the version on the host is cheaper.

**What breaks if you ignore it** — the running server reports `0.0.0.0` when asked for its version, so
you cannot tell from inside the pod which clio it is; correlate through the image tag instead. If a
build ever does need the real number inside the assembly, pass it in as a build argument and forward
it as `/p:Version=` to `dotnet publish` - adding `.git` to the build context is the wrong fix.
