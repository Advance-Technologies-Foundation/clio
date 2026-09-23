# Local workspace attachment to an operator runtime

Status: in-progress

Implement attach/detach CLI commands, operator provider, local dependency preflight, safe Mutagen lifecycle and durable ownership receipt. Document prerequisites and scope. Validate two-way synchronization against a disposable runtime and preservation after detach.

Acceptance: missing dependencies cause no remote mutation; multiple workspaces have distinct remote paths/sessions; package collisions fail safely; repeated attach resumes the same attachment; detach cannot destroy environment or files; SSH identity is verified.
