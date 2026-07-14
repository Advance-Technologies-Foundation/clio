# Story: reliable Explorer deployment defaults

Status: review

As a Windows user deploying a Creatio ZIP from Explorer, I want clio to use my unambiguous local infrastructure preference and keep failures visible so that deployment does not unexpectedly depend on an unavailable Kubernetes cluster.

## Acceptance criteria

- The sole enabled local database server is selected when no explicit or deploy-specific database server is set.
- Explicit and deploy-specific selections are not overridden.
- Zero or multiple enabled local database servers preserve Kubernetes fallback behavior.
- Default local Redis resolution remains unchanged.
- Explorer pauses after failure and closes after success.
- Targeted command tests and documentation consistency checks pass.
