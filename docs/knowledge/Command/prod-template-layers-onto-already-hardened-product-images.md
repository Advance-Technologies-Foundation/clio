---
description: the prod docker template's /conf and /Terrasoft.Configuration links must stay idempotent - the Creatio .NET 10 product image already has them and the k8s control plane layers prod builds onto product images via --base-image ("ln: failed to create symbolic link '/conf': File exists")
applies-to:
  - clio/tpl/docker-templates/prod/Dockerfile
ticket: ENG-94408
date: 2026-09-23
---

**What is true** — the Creatio .NET 10 product image already carries the OpenShift hardening links
`/conf -> /app/conf` and `/Terrasoft.Configuration -> /app/Terrasoft.Configuration`, created with
exactly `ln -sT /app/conf /conf` and `ln -sT /app/Terrasoft.Configuration /Terrasoft.Configuration`
in core `TSBpm/Src/Lib/Terrasoft.WebHost/Dockerfile` (ENG-95158). The Creatio Kubernetes control plane
builds platform-update images with `clio build-docker-image --template prod --base-image <product image
or previous prod image>`, so the prod template routinely runs on top of an image that has both links
and a populated `/app`.

**Why it is this way** — neither side owns the other's Dockerfile: core hardens the product image for
its own consumers, and clio's template repeats the hardening so a plain `creatio-base` build is hardened
too. Both use the same literal link targets, which is why the template's `ensure_link` compares
`readlink` output exactly instead of resolving it.

**What breaks if you ignore it** — an unconditional `ln -sT` fails every such build with
`ln: failed to create symbolic link '/conf': File exists` (this shipped in 8.1.0.113 and blocked
platform updates). Adding `-f` instead would silently re-point a `/conf` that some other base image
uses for something else. If core ever changes the link targets (for example to relative ones), the
template fails loudly with its "is not a symlink to /app/conf" error and `ensure_link` must be
re-verified against the new core layout rather than loosened.
