using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Creatio.ConflictResolver;

internal sealed class ResourceXmlConflictMarkerFormatter : IAutomergeConflictFormatter
{
	public bool CanFormat(MergeRequest request, MergeResult result)
	{
		return request.FileType == ConflictFileType.ResourceXml &&
		       !string.IsNullOrWhiteSpace(result.MergedContent);
	}

	public string? TryFormat(MergeRequest request, MergeResult result, IReadOnlyCollection<string> conflictTokens)
	{
		if (string.IsNullOrWhiteSpace(result.MergedContent))
		{
			return null;
		}

		return TryFormatResourceXml(request.Local, request.Remote, result.MergedContent!, conflictTokens);
	}

	private static string? TryFormatResourceXml(
		string localContent,
		string remoteContent,
		string mergedContent,
		IReadOnlyCollection<string> conflictKeys)
	{
		if (!TryParseXml(localContent, out var localDocument) ||
		    !TryParseXml(remoteContent, out var remoteDocument) ||
		    !TryParseXml(mergedContent, out var mergedDocument))
		{
			return null;
		}

		var mergedItems = GetItems(mergedDocument!);
		if (conflictKeys.Count == 0)
		{
			return null;
		}

		var localIndex = BuildIndex(localDocument!);
		var remoteIndex = BuildIndex(remoteDocument!);
		var newLine = DetectNewLine(mergedContent);
		var lines = SplitLines(mergedContent);
		if (!TryFindItemsBlock(lines, out var openIndex, out var closeIndex))
		{
			return null;
		}

		var containerIndent = GetLeadingWhitespace(lines[openIndex]);
		var itemIndent = containerIndent + "\t";
		var renderedItems = new List<string>();
		var conflictSet = conflictKeys.ToHashSet(StringComparer.Ordinal);
		var renderedKeys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in mergedItems)
		{
			var key = (string?)item.Attribute("Name");
			if (string.IsNullOrWhiteSpace(key))
			{
				renderedItems.AddRange(IndentLines(SerializeElement(item), itemIndent));
				continue;
			}

			renderedKeys.Add(key!);

				if (!conflictSet.Contains(key!))
			{
				renderedItems.AddRange(IndentLines(SerializeElement(item), itemIndent));
				continue;
			}

			renderedItems.Add("<<<<<<< Local");
			if (localIndex.TryGetValue(key!, out var localItem))
			{
				renderedItems.AddRange(IndentLines(SerializeElement(localItem), itemIndent));
			}

			renderedItems.Add("=======");
			if (remoteIndex.TryGetValue(key!, out var remoteItem))
			{
				renderedItems.AddRange(IndentLines(SerializeElement(remoteItem), itemIndent));
			}

			renderedItems.Add(">>>>>>> Remote");
		}

		foreach (var key in conflictSet.Except(renderedKeys).OrderBy(static key => key, StringComparer.Ordinal))
		{
			renderedItems.Add("<<<<<<< Local");
			if (localIndex.TryGetValue(key, out var localItem))
			{
				renderedItems.AddRange(IndentLines(SerializeElement(localItem), itemIndent));
			}

			renderedItems.Add("=======");
			if (remoteIndex.TryGetValue(key, out var remoteItem))
			{
				renderedItems.AddRange(IndentLines(SerializeElement(remoteItem), itemIndent));
			}

			renderedItems.Add(">>>>>>> Remote");
		}

		var outputLines = new List<string>(lines.Count - Math.Max(0, closeIndex - openIndex - 1) + renderedItems.Count);
		outputLines.AddRange(lines.Take(openIndex + 1));
		outputLines.AddRange(renderedItems);
		outputLines.AddRange(lines.Skip(closeIndex));
		return string.Join(newLine, outputLines);
	}

	private static bool TryParseXml(string content, out XDocument? document)
	{
		try
		{
			document = SafeXmlDocumentParser.Parse(content);
			return true;
		}
		catch (XmlException)
		{
			document = null;
			return false;
		}
	}

	private static IReadOnlyList<XElement> GetItems(XDocument document)
	{
		return document
			.Descendants()
			.FirstOrDefault(static x => x.Name.LocalName == "Items")?
			.Elements()
			.Where(static x => x.Name.LocalName == "Item")
			.Select(static x => new XElement(x))
			.ToArray() ?? Array.Empty<XElement>();
	}

	private static IReadOnlyDictionary<string, XElement> BuildIndex(XDocument document)
	{
		var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
		foreach (var item in GetItems(document))
		{
			var key = (string?)item.Attribute("Name");
			if (!string.IsNullOrWhiteSpace(key))
			{
					result[key!] = item;
			}
		}

		return result;
	}

	private static bool TryFindItemsBlock(IReadOnlyList<string> lines, out int openIndex, out int closeIndex)
	{
		openIndex = -1;
		closeIndex = -1;
		for (var i = 0; i < lines.Count; i++)
		{
			var trimmed = lines[i].Trim();
			if (openIndex < 0 && trimmed.StartsWith("<Items", StringComparison.Ordinal))
			{
				openIndex = i;
				continue;
			}

			if (openIndex >= 0 && string.Equals(trimmed, "</Items>", StringComparison.Ordinal))
			{
				closeIndex = i;
				break;
			}
		}

		return openIndex >= 0 && closeIndex > openIndex;
	}

	private static IReadOnlyList<string> IndentLines(string text, string indent)
	{
		return SplitLines(text)
			.Select(line => indent + line)
			.ToArray();
	}

	private static string SerializeElement(XElement element)
	{
		var settings = new XmlWriterSettings
		{
			Indent = true,
			IndentChars = "\t",
			NewLineChars = "\n",
			NewLineHandling = NewLineHandling.Replace,
			OmitXmlDeclaration = true
		};

		using var writer = new Utf8StringWriter();
		using var xmlWriter = XmlWriter.Create(writer, settings);
		element.Save(xmlWriter);
		xmlWriter.Flush();
		return writer.ToString().Trim();
	}

	private static IReadOnlyList<string> SplitLines(string content) =>
		content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

	private static string GetLeadingWhitespace(string line)
	{
		var index = 0;
		while (index < line.Length && char.IsWhiteSpace(line[index]))
		{
			index++;
		}

		return line.Substring(0, index);
	}

	private static string DetectNewLine(string content) =>
		content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

	private sealed class Utf8StringWriter : StringWriter
	{
		public override Encoding Encoding => Encoding.UTF8;
	}
}
