using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
	public class PropertiesJsonMergeStrategyTests
	{
	[Test]
	[Description("Rejects duplicate UIds before a keyed JSON array can discard a branch value.")]
	public void PropertiesMerge_DuplicateRemoteUId_ReturnsInvalidInput()
	{
		// Arrange
		const string baseContent = """{"Properties":[{"UId":"x","Value":"base"}]}""";
		const string remoteContent = """{"Properties":[{"UId":"x","Value":"hidden-remote"},{"UId":"x","Value":"base"}]}""";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.PropertiesJson,
			baseContent,
			baseContent,
			remoteContent));

		// Assert
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
			"duplicate semantic keys must not be indexed with last-write-wins behavior");
		Assert.That(result.ErrorCode, Is.EqualTo("DuplicateRemoteSemanticKey"),
			"the diagnostic must identify the branch containing the duplicate");
	}

	[Test]
	[Description("Stops pretty JSON serialization at the four MiB output budget.")]
	public void PropertiesMerge_ExpandedOutput_ReturnsInvalidInput()
	{
		// Arrange
		string nestedArray = new string('[', 55) + string.Join(",", Enumerable.Repeat("0", 120_000)) + new string(']', 55);
		string content = "{\"Properties\":{\"Large\":" + nestedArray + "}}";

		// Act
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.PropertiesJson,
			content,
			content,
			content));

		// Assert
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.InvalidInput),
			"a compact request must not allocate an unbounded pretty-printed result");
		Assert.That(result.ErrorCode, Is.EqualTo("MergeOutputLimitExceeded"),
			"the caller needs the stable output-budget diagnostic");
	}

	[Test]
	public void PropertiesMerge_FixtureCase1_LocalWinsBaseUriConflictAndKeepsLocalType()
	{
		var (baseContent, localContent, remoteContent, expected) = ReadFixture("propertiesTestCases1");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.PropertiesJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
		var properties = GetProperties(result.MergedContent!);
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.VerificationPassed, Is.True);
		Assert.That(GetStringValue(properties, "BaseUri"), Is.EqualTo("http://localhost/SOAPService2Local"));
		Assert.That(GetStringValue(properties, "CreatedInVersion"), Is.EqualTo("8.3.3.2060"));
		Assert.That(GetStringValue(properties, "Type"), Is.EqualTo("Soap12"));
		Assert.That(
			result.Report.TrueConflicts,
			Is.EqualTo(new[] { "$.Properties.BaseUri" }));
	}

	[Test]
	public void PropertiesMerge_FixtureCase2_LocalWinsBaseUriConflict()
	{
		var (baseContent, localContent, remoteContent, expected) = ReadFixture("propertiesTestCases2");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.PropertiesJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expected));
		var properties = GetProperties(result.MergedContent!);
		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.WinnerPolicy, Is.EqualTo("LOCAL"));
		Assert.That(result.Report.VerificationPassed, Is.True);
		Assert.That(GetStringValue(properties, "BaseUri"), Is.EqualTo("http://localhost/rest1RemoteLocal"));
		Assert.That(GetStringValue(properties, "CreatedInVersion"), Is.EqualTo("8.3.3.2060"));
		Assert.That(GetStringValue(properties, "Type"), Is.EqualTo("Rest"));
		Assert.That(
			result.Report.TrueConflicts,
			Is.EqualTo(new[] { "$.Properties.BaseUri" }));
	}

	private static (string BaseContent, string LocalContent, string RemoteContent, string Expected) ReadFixture(string fixtureCase)
	{
		var baseContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(fixtureCase, "base.json"));
		var localContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(fixtureCase, "local.json"));
		var remoteContent = File.ReadAllText(ResolverTestSupport.GetFixturePath(fixtureCase, "remote.json"));
		var expected = File.ReadAllText(ResolverTestSupport.GetFixturePath(fixtureCase, "resolved.json"));
		return (baseContent, localContent, remoteContent, expected);
	}

	private static JsonObject GetProperties(string mergedContent)
	{
		var root = JsonNode.Parse(mergedContent)!.AsObject();
		return root["Properties"]!.AsObject();
	}

	private static string GetStringValue(JsonObject obj, string propertyName)
	{
		return obj[propertyName]!.GetValue<string>();
	}
}
