using System.Xml.Linq;
using System.Xml;
using System.Text;

namespace Creatio.ConflictResolver.Strategies;

internal sealed class ResourceMergeStrategy : IMergeStrategy
{
	private static readonly StringComparer KeyComparer = StringComparer.Ordinal;

	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.ResourceXml;

	public MergeResult Merge(MergeRequest request)
	{
		if (!TryParseXml(request.Base, out var baseDocument, out var baseError))
		{
			return MergeResultFactory.InvalidInput("InvalidBaseXml", baseError);
		}

		if (!TryParseXml(request.Local, out var localDocument, out var localError))
		{
			return MergeResultFactory.InvalidInput("InvalidLocalXml", localError);
		}

		if (!TryParseXml(request.Remote, out var remoteDocument, out var remoteError))
		{
			return MergeResultFactory.InvalidInput("InvalidRemoteXml", remoteError);
		}

		if (HasDuplicateKeys(baseDocument!, out var baseDuplicates))
		{
			return MergeResultFactory.InvalidInput("DuplicateBaseKeys", $"Base resource XML has duplicate keys: {string.Join(", ", baseDuplicates)}");
		}

		if (HasDuplicateKeys(localDocument!, out var localDuplicates))
		{
			return MergeResultFactory.InvalidInput("DuplicateLocalKeys", $"Local resource XML has duplicate keys: {string.Join(", ", localDuplicates)}");
		}

		if (HasDuplicateKeys(remoteDocument!, out var remoteDuplicates))
		{
			return MergeResultFactory.InvalidInput("DuplicateRemoteKeys", $"Remote resource XML has duplicate keys: {string.Join(", ", remoteDuplicates)}");
		}

		var baseIndex = BuildIndex(baseDocument!);
		var localIndex = BuildIndex(localDocument!);
		var remoteIndex = BuildIndex(remoteDocument!);

		if (localIndex.ItemsContainer is null)
		{
			return MergeResultFactory.InvalidInput("MissingItems", "Local resource XML has no <Items> node.");
		}

		var baseKeys = baseIndex.Order.ToHashSet(KeyComparer);
		var localKeys = localIndex.Order.ToHashSet(KeyComparer);
		var remoteKeys = remoteIndex.Order.ToHashSet(KeyComparer);

		var localAdditions = localIndex.Order.Where(key => !baseKeys.Contains(key)).ToArray();
		var remoteAdditions = remoteIndex.Order.Where(key => !baseKeys.Contains(key)).ToArray();
		var localDeletions = baseIndex.Order.Where(key => !localKeys.Contains(key)).ToArray();
		var remoteDeletions = baseIndex.Order.Where(key => !remoteKeys.Contains(key)).ToArray();
		var trueConflicts = DetectTrueConflicts(baseIndex, localIndex, remoteIndex);
		var allDeletions = localDeletions
			.Concat(remoteDeletions)
			.Except(trueConflicts, KeyComparer)
			.ToHashSet(KeyComparer);
		var mergedOrder = BuildResultOrder(
			baseIndex.Order,
			localAdditions,
			remoteAdditions,
			allDeletions);

		var mergedItems = new List<XElement>(mergedOrder.Count);
		foreach (var key in mergedOrder)
		{
			var hasBase = baseIndex.Map.TryGetValue(key, out var baseValue);
			var hasLocal = localIndex.Map.TryGetValue(key, out var localValue);
			var hasRemote = remoteIndex.Map.TryGetValue(key, out var remoteValue);

			XElement? winner = null;
			if (hasLocal && hasRemote)
			{
				winner = ChooseEntry(
					hasBase ? baseValue : null,
					localValue!,
					remoteValue!);
			}
			else if (hasLocal)
			{
				winner = new XElement(localValue!);
			}
			else if (hasRemote)
			{
				winner = new XElement(remoteValue!);
			}
			else if (hasBase)
			{
				winner = new XElement(baseValue!);
			}

			if (winner is not null)
			{
				mergedItems.Add(winner);
			}
		}

		localIndex.ItemsContainer.ReplaceNodes(mergedItems);

		if (HasDuplicateKeys(localDocument!, out var duplicates))
		{
			return MergeResultFactory.UnresolvedConflict(
				"DuplicateKeys",
				$"Merged resource XML has duplicate keys: {string.Join(", ", duplicates)}",
				"name_union_local_win",
				trueConflicts: trueConflicts,
				mergedContent: SerializeMergedXml(localDocument!),
				localAdditions: localAdditions,
				remoteAdditions: remoteAdditions,
				localDeletions: localDeletions,
				remoteDeletions: remoteDeletions,
				verificationPassed: false);
		}

		var mergedContent = SerializeMergedXml(localDocument!);
		return MergeResultFactory.Resolved(
			mergedContent,
			"name_union_local_win",
			localAdditions: localAdditions,
			remoteAdditions: remoteAdditions,
			localDeletions: localDeletions,
			remoteDeletions: remoteDeletions,
			trueConflicts: trueConflicts,
			verificationPassed: true);
	}

	private static XElement ChooseEntry(XElement? baseValue, XElement localValue, XElement remoteValue)
	{
		if (XNode.DeepEquals(localValue, remoteValue))
		{
			return new XElement(localValue);
		}

		if (baseValue is not null && XNode.DeepEquals(localValue, baseValue))
		{
			return new XElement(remoteValue);
		}

		if (baseValue is not null && XNode.DeepEquals(remoteValue, baseValue))
		{
			return new XElement(localValue);
		}

		return new XElement(localValue);
	}

	private static List<string> DetectTrueConflicts(ResourceIndex baseIndex, ResourceIndex localIndex, ResourceIndex remoteIndex)
	{
		var result = new HashSet<string>(KeyComparer);
		var keys = baseIndex.Map.Keys
			.Concat(localIndex.Map.Keys)
			.Concat(remoteIndex.Map.Keys)
			.Distinct(KeyComparer);
		foreach (var key in keys)
		{
			var hasBase = baseIndex.Map.TryGetValue(key, out var baseValue);
			var hasLocal = localIndex.Map.TryGetValue(key, out var localValue);
			var hasRemote = remoteIndex.Map.TryGetValue(key, out var remoteValue);
			if (hasBase && hasLocal != hasRemote)
			{
				var survivingValue = hasLocal ? localValue! : remoteValue!;
				if (!XNode.DeepEquals(survivingValue, baseValue))
				{
					result.Add(key);
				}

				continue;
			}

			if (!hasLocal || !hasRemote)
			{
				continue;
			}

			if (XNode.DeepEquals(localValue, remoteValue))
			{
				continue;
			}

			if (hasBase)
			{
				var localChanged = !XNode.DeepEquals(localValue, baseValue);
				var remoteChanged = !XNode.DeepEquals(remoteValue, baseValue);
				if (localChanged && remoteChanged)
				{
					result.Add(key);
				}

				continue;
			}

			result.Add(key);
		}

		return result.OrderBy(static x => x, StringComparer.Ordinal).ToList();
	}

	private static List<string> BuildResultOrder(
		IReadOnlyList<string> baseOrder,
		IEnumerable<string> localAdditions,
		IEnumerable<string> remoteAdditions,
		ISet<string> allDeletions)
	{
		var targetKeys = new HashSet<string>(KeyComparer);

		foreach (var key in baseOrder)
		{
			if (!allDeletions.Contains(key))
			{
				targetKeys.Add(key);
			}
		}

		foreach (var key in localAdditions)
		{
			if (!allDeletions.Contains(key))
			{
				targetKeys.Add(key);
			}
		}

		foreach (var key in remoteAdditions)
		{
			if (!allDeletions.Contains(key))
			{
				targetKeys.Add(key);
			}
		}

		return targetKeys
			.OrderBy(static x => x, StringComparer.Ordinal)
			.ToList();
	}

	private static bool HasDuplicateKeys(XDocument document, out IReadOnlyList<string> duplicates)
	{
		duplicates = document
			.Descendants()
			.Where(static x => x.Name.LocalName == "Item")
			.Select(static x => (string?)x.Attribute("Name"))
			.Where(static x => !string.IsNullOrWhiteSpace(x))
			.GroupBy(static x => x!, KeyComparer)
			.Where(static g => g.Count() > 1)
			.Select(static g => g.Key)
			.OrderBy(static x => x, StringComparer.Ordinal)
			.ToArray();

		return duplicates.Count > 0;
	}

	private static ResourceIndex BuildIndex(XDocument document)
	{
		var container = document
			.Descendants()
			.FirstOrDefault(static x => x.Name.LocalName == "Items");

		var order = new List<string>();
		var map = new Dictionary<string, XElement>(KeyComparer);
		if (container is null)
		{
			return new ResourceIndex(map, order, null);
		}

		foreach (var item in container.Elements().Where(static x => x.Name.LocalName == "Item"))
		{
			var key = (string?)item.Attribute("Name");
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

				if (!map.ContainsKey(key!))
			{
					map[key!] = new XElement(item);
					order.Add(key!);
			}
		}

		return new ResourceIndex(map, order, container);
	}

	private static bool TryParseXml(string content, out XDocument? document, out string error)
	{
		try
		{
			document = SafeXmlDocumentParser.Parse(content);
			error = string.Empty;
			return true;
		}
		catch (XmlException ex)
		{
			document = null;
			error = ex.Message;
			return false;
		}
	}

	private static string SerializeMergedXml(XDocument document)
	{
		foreach (var text in document
		             .DescendantNodes()
		             .OfType<XText>()
		             .Where(static x => string.IsNullOrWhiteSpace(x.Value))
		             .ToArray())
		{
			text.Remove();
		}

		document.Declaration = new XDeclaration("1.0", "utf-8", null);

		var settings = new XmlWriterSettings
		{
			Indent = true,
			IndentChars = "\t",
			NewLineChars = "\r\n",
			NewLineHandling = NewLineHandling.Replace,
			OmitXmlDeclaration = false
		};

		using var writer = new Utf8StringWriter();
		using var xmlWriter = XmlWriter.Create(writer, settings);
		document.Save(xmlWriter);
		xmlWriter.Flush();
		return writer.ToString().TrimStart('\r', '\n');
	}

	private sealed class Utf8StringWriter : StringWriter
	{
		public override Encoding Encoding => Encoding.UTF8;
	}

	private sealed record ResourceIndex(
		IReadOnlyDictionary<string, XElement> Map,
		IReadOnlyList<string> Order,
		XElement? ItemsContainer);
}
