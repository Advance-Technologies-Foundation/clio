using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Adds verified, currently active knowledge articles to MCP resource discovery.
/// </summary>
internal static class KnowledgeResourceDiscoveryFilter {
	internal const int DynamicPageSize = 100;
	internal const string DynamicCursorPrefix = "clio-knowledge-v1:";
	private const int CursorOffsetLength = 10;

	/// <summary>
	/// Wraps the standard resource-list handler and appends dynamically delivered knowledge resources.
	/// </summary>
	/// <param name="next">The next MCP request handler.</param>
	/// <returns>The wrapped request handler.</returns>
	internal static McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> AppendKnowledgeResources(
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next) {
		ArgumentNullException.ThrowIfNull(next);
		return async (request, cancellationToken) => {
			string? cursor = request.Params.Cursor;
			if (cursor?.StartsWith(DynamicCursorPrefix, StringComparison.Ordinal) == true) {
				int offset = ParseDynamicCursor(cursor);
				IKnowledgeGuidanceSource? dynamicSource = request.Services?.GetService<IKnowledgeGuidanceSource>();
				return dynamicSource is null
					? new ListResourcesResult { Resources = [] }
					: CreateDynamicPage(dynamicSource, offset, new HashSet<string>(StringComparer.Ordinal));
			}

			ListResourcesResult result = await next(request, cancellationToken).ConfigureAwait(false);
			if (result.NextCursor is not null) {
				return result;
			}
			IKnowledgeGuidanceSource? source = request.Services?.GetService<IKnowledgeGuidanceSource>();
			if (source is null) {
				return result;
			}
			HashSet<string> existingUris = result.Resources
				.Select(resource => resource.Uri)
				.ToHashSet(StringComparer.Ordinal);
			ListResourcesResult dynamicPage = CreateDynamicPage(source, offset: 0, existingUris);
			foreach (Resource resource in dynamicPage.Resources) {
				result.Resources.Add(resource);
			}
			result.NextCursor = dynamicPage.NextCursor;
			return result;
		};
	}

	private static ListResourcesResult CreateDynamicPage(
		IKnowledgeGuidanceSource source,
		int offset,
		IReadOnlySet<string> existingUris) {
		KnowledgeGuidanceDescriptor[] catalog = source.GetCatalog()
			.OrderBy(article => article.Uri, StringComparer.Ordinal)
			.ThenBy(article => article.Name, StringComparer.Ordinal)
			.DistinctBy(article => article.Uri, StringComparer.Ordinal)
			.ToArray();
		List<Resource> resources = [];
		int index = Math.Min(offset, catalog.Length);
		while (index < catalog.Length && resources.Count < DynamicPageSize) {
			KnowledgeGuidanceDescriptor article = catalog[index++];
			if (existingUris.Contains(article.Uri)) {
				continue;
			}
			resources.Add(ToResource(article));
		}
		while (index < catalog.Length && existingUris.Contains(catalog[index].Uri)) {
			index++;
		}
		return new ListResourcesResult {
			Resources = resources,
			NextCursor = index < catalog.Length ? CreateDynamicCursor(index) : null
		};
	}

	private static Resource ToResource(KnowledgeGuidanceDescriptor article) => new() {
		Name = article.Name,
		Title = article.Title,
		Description = article.Description,
		Uri = article.Uri,
		MimeType = article.MediaType
	};

	private static string CreateDynamicCursor(int offset) =>
		DynamicCursorPrefix + offset.ToString($"D{CursorOffsetLength}", CultureInfo.InvariantCulture);

	private static int ParseDynamicCursor(string cursor) {
		ReadOnlySpan<char> offset = cursor.AsSpan(DynamicCursorPrefix.Length);
		if (offset.Length != CursorOffsetLength
				|| !int.TryParse(offset, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) {
			throw new McpProtocolException("The knowledge resource cursor is invalid.", McpErrorCode.InvalidParams);
		}
		return value;
	}
}
