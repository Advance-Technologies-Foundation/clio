# Reuse native Classic process-page primitives

Use one DI service shared by the CLI and an offline MCP adapter. Inherit
ProcessFlowElementPropertiesPage and generate MAPPING attributes with the native
initPropertySilent/doAutoSave and autocomplete bindings. Store the page identity
in the task's FK11 metadata and images in its native resource XML.

The output remains ordinary editable workspace source. No runtime framework,
general ExtJS builder, registration orchestration or deployment state is added.
The flat MCP adapter avoids BaseTool's eager environment resolution for this
explicitly offline operation. Existing pages are refused rather than overwritten.

Validate identities, collisions and SVG content before writing. Accept static SVG
shapes only; reject external references, processing instructions and base overrides.
