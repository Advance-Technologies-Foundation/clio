using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing catalog of Creatio <c>--crt-*</c> design tokens. Its text is the single source shared with the
/// ui-project scaffold: <c>tpl/ui-project/ai-guides/DESIGN_TOKENS_AI_GUIDE.md</c>, embedded into the assembly so the
/// MCP guidance and the scaffolded project copy never diverge.
/// </summary>
[McpServerResourceType]
public sealed class DesignTokensGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/design-tokens";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;
	private const string EmbeddedResourceName = "DESIGN_TOKENS_AI_GUIDE.md";

	[McpServerResource(UriTemplate = ResourceUri, Name = "design-tokens-guidance")]
	[Description("Returns the canonical catalog of Creatio --crt-* design tokens: semantic colors (text, background, border, icon), typography roles, palettes, spacing/radius primitives, and font weights, with token names and default-theme values.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = LoadEmbeddedGuide()
	};

	private static string LoadEmbeddedGuide() {
		Assembly assembly = typeof(DesignTokensGuidanceResource).Assembly;
		using Stream stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
			?? throw new InvalidOperationException(
				$"Embedded design-tokens guide '{EmbeddedResourceName}' was not found in the assembly manifest.");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}
}
