# Story: reliable Explorer deployment defaults

Status: done

As a Windows user deploying a Creatio ZIP from Explorer, I want clio to use my unambiguous local infrastructure preference and keep failures visible so that deployment does not unexpectedly depend on an unavailable Kubernetes cluster.

## Acceptance criteria

- The sole enabled local database server is selected for Explorer when no explicit or deploy-specific database server is set.
- MCP and ClioRing Kubernetes selections remain Kubernetes when a sole local server exists.
- Explicit and deploy-specific selections are not overridden.
- Zero or multiple enabled local database servers preserve Kubernetes fallback behavior.
- Default local Redis resolution remains unchanged.
- Explorer pauses exactly once after failure and does not add a command-level exit prompt.
- Targeted command tests and documentation consistency checks pass.
