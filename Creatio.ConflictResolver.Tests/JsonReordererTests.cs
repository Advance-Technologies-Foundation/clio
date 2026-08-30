using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class JsonReordererTests
{
	[Test]
	public void MetadataMerge_FlatJsonReordererCase_MatchesExpectedOrder1441820307()
	{
		var transplier = new global::Creatio.ConflictResolver.FlatDiffTranspiler();
		var baseTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "base.json"));
		var localTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "local.json"));
		var remoteTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "remote.json"));
		var expectedTransformed = File.ReadAllText(ResolverTestSupport.GetFixturePath("TestCases\\JsonReordererCase1", "mergedOrderedExample.json"));
		var baseContent = transplier.Restore(baseTransformed);
		var localContent = transplier.Restore(localTransformed);
		var remoteContent = transplier.Restore(remoteTransformed);

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		var actualTransformed = transplier.Transform(result.MergedContent!);
		Assert.That(
			ReadItemUids(actualTransformed),
			Is.EqualTo(ReadItemUids(expectedTransformed)));
	}

	public static IReadOnlyList<string> ReadItemUids(string transformedMetadata)
	{
		var root = JsonNode.Parse(transformedMetadata)!.AsObject();
		var items = root["Items"]!.AsArray();
		return items
			.OfType<JsonObject>()
			.Select(static item => item["UId"]!.GetValue<string>())
			.ToArray();
	}
}
