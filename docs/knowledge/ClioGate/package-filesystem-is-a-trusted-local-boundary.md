---
description: ClioGate package path checks reject existing reparse points but do not provide atomic no-follow filesystem access
applies-to:
  - cliogate/Files/cs/CreatioApiGateway.cs
ticket: "#1216"
date: 2026-08-30
---

**What is true** — ClioGate treats the Creatio application package tree as a trusted local
filesystem boundary. It rejects traversal, rooted paths, and existing symbolic links, junctions,
or reparse points, but its .NET Framework-compatible file operations do not atomically combine
that validation with opening the final target.

**Why it is this way** — ClioGate's authenticated package endpoints can create ordinary files and
directories but cannot create reparse points. Exploiting the remaining check-to-use interval
therefore requires independent local write access to the Creatio application tree, which already
crosses the privilege boundary this API is designed to protect. Cross-platform handle-relative
no-follow access would add platform-specific native code to defend against an actor that already
controls the application files.

**What breaks if you ignore it** — Do not claim these checks protect against a hostile local
filesystem administrator or process running with application-directory write access. If that
becomes part of the threat model, replace path-based reads, writes, and deletes with an atomic
handle-relative/no-follow design and validate the opened target before exposing it remotely.
