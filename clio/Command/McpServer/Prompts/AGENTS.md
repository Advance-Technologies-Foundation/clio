# MCP Prompt Rules

This directory contains MCP prompt declarations discovered through type and method attributes.

## Class shape

- Prompt containers must be declared as `public static class`.
- Do not add public or protected constructors to prompt containers.
- If a prompt needs runtime state or injected services, move that logic to another service and keep the prompt class as a thin static formatter.

## API design

- Keep prompt methods `public static`.
- Add XML documentation comments to every public prompt class and public prompt method.
- Use `[McpServerPromptType]` on the class and `[McpServerPrompt(...)]` on each exported prompt method.
- Keep prompt method names and prompt `Name` values stable unless the MCP contract intentionally changes.

## Prompt content

- Keep prompt text concise and specific to the corresponding MCP tool or resource.
- Refer to MCP tool names, argument names, and destructive behavior exactly as implemented.
- Do not expose secrets in prompt text beyond what the current contract already requires.
- Prefer backticked placeholders and argument names in generated prompt text.

## Maintenance

- When adding a new prompt file in this folder, follow these rules by default.
- When modifying an existing prompt, leave it in `public static class` utility form.
