---
description: Updates style of unit tests
name: unittests-style
model: Claude Sonnet 4.5 (copilot)
tools: ['vscode', 'execute', 'read', 'edit', 'search', 'web', 'context7/*', 'agent', 'todo']
---
Given a unit test class name ${input:class_name}, locate the corresponding unit test file in the codebase.

- Every test must be decorated with Description attribute that clearly states what the test is verifying.
- Every Test Must have 3 areas (Arrange, Act, Assert) for better readability. Use comment to clearly separate these areas.
- Every assertion must have a message (because) that will be displayed when the assertion fails.