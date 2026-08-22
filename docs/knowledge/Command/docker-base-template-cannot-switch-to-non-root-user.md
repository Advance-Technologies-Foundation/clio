---
description: adding USER to docker-templates/base/Dockerfile breaks dev/prod builds
applies-to:
  - clio/tpl/docker-templates/base/Dockerfile
  - clio/tpl/docker-templates/dev/Dockerfile
  - clio/tpl/docker-templates/prod/Dockerfile
ticket: GH-1161
date: 2026-08-21
---

**What is true** — `base/Dockerfile` has no `ENTRYPOINT`/`CMD` of its own; it only exists as the
shared `FROM ${BASE_IMAGE}` layer that `dev/Dockerfile` and `prod/Dockerfile` build on
(`BuildDockerImageService`). Neither leaf template re-elevates with `USER root` before its own
`RUN` steps.

**Why it is this way** — `dev/Dockerfile` needs root for its own build-time `RUN` steps (`apt-get
install`, `chpasswd`, writing under `/root`) and to run `supervisord`/`sshd` at container start.
`prod/Dockerfile` has no such steps — it only runs `supervisord`/`creatio-webhost`, an
unprivileged process — and stays root only because it shares this same base file with `dev`, not
because it independently needs root. Both are deliberately out of scope for the GH #1161
root-hardening pass; a `prod`-only hardening pass (its own `USER` plus the `chown` that entails)
is a separate, deferred follow-up.

**What breaks if you ignore it** — adding a `USER <non-root>` instruction to `base/Dockerfile`
(the obvious fix for its own missing-`USER` Sonar hotspot) is inherited by every stage `FROM
${BASE_IMAGE}`. `dev`'s subsequent `RUN apt-get ...` etc. would then execute as that non-root user
and fail with permission errors, breaking its Docker build — a regression in a file this fix is
explicitly not allowed to touch. Do not add `USER` to `base/Dockerfile` without also
auditing/updating `dev` (required) and `prod` (as its own deferred follow-up) to handle
non-root correctly.
