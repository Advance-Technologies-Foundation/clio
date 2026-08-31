using System.Text.Json.Nodes;
using Creatio.ConflictResolver.Tests.TestSupport;

namespace Creatio.ConflictResolver.Tests;

[TestFixture, Category("Unit")]
public class MetadataTimelineEntityMergeStrategyTests
{
	[Test]
	public void MetadataMerge_FixtureCase1_LegacyArrayKeepsLocalVersionOnConflict()
	{
		var baseContent = ReadFixtureFile("Case1", "metadata-base.json");
		var localContent = ReadFixtureFile("Case1", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case1", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case1", "merge-expected.json");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			NormalizeFixtureContent(result.MergedContent!),
			Is.EqualTo(NormalizeFixtureContent(expectedContent)));
	}

	[Test]
	public void MetadataMerge_FixtureCase2_LegacyArrayUsesLocalShape()
	{
		var baseContent = ReadFixtureFile("Case2", "metadata-base.json");
		var localContent = ReadFixtureFile("Case2", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case2", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case2", "merge-expected.json");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			NormalizeFixtureContent(result.MergedContent!),
			Is.EqualTo(NormalizeFixtureContent(expectedContent)));
	}

	[Test]
	public void MetadataMerge_FixtureCase3_MergesOuterPropertiesAndKeepsLegacyArrayValid()
	{
		var baseContent = ReadFixtureFile("Case3", "metadata-base.json");
		var localContent = ReadFixtureFile("Case3", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case3", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case3", "merge-expected.json");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			NormalizeFixtureContent(result.MergedContent!),
			Is.EqualTo(NormalizeFixtureContent(expectedContent)));
	}

	[Test]
	public void MetadataMerge_FixtureCase4_MergesRootElemsAndApplyLocalEntityValues()
	{
		var baseContent = ReadFixtureFile("Case4", "metadata-base.json");
		var localContent = ReadFixtureFile("Case4", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case4", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case4", "merge-expected.json");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expectedContent));
	}

	[Test]
	public void MetadataMerge_NewFormat_MergesTimelineValuesByUidAndColumnLayoutsByColumnName()
	{
		const string baseContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "ManagerName": "AddonSchemaManager",
		      "AD3": "TimelineEntity",
		      "AD4": {
		        "SchemaName": "Activity",
		        "TimelineEntityValues": [
		          {
		            "UId": "timeline-1",
		            "TypeColumnValue": "type-1",
		            "OwnerColumn": "CreatedBy",
		            "ColumnLayouts": [
		              {
		                "ColumnName": "CreatedOn",
		                "ColumnLayout": null
		              },
		              {
		                "ColumnName": "Title",
		                "ColumnLayout": null
		              }
		            ]
		          }
		        ]
		      }
		    }
		  }
		}
		""";
		const string localContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "ManagerName": "AddonSchemaManager",
		      "AD3": "TimelineEntity",
		      "AD4": {
		        "SchemaName": "Activity",
		        "TimelineEntityValues": [
		          {
		            "UId": "timeline-1",
		            "TypeColumnValue": "type-1",
		            "OwnerColumn": "LocalOwner",
		            "ColumnLayouts": [
		              {
		                "ColumnName": "CreatedOn",
		                "ColumnLayout": null
		              },
		              {
		                "ColumnName": "Title",
		                "ColumnLayout": "{\"column\":2}"
		              },
		              {
		                "ColumnName": "Status",
		                "ColumnLayout": null
		              }
		            ]
		          }
		        ]
		      }
		    }
		  }
		}
		""";
		const string remoteContent = """
		{
		  "MetaData": {
		    "Schema": {
		      "ManagerName": "AddonSchemaManager",
		      "AD3": "TimelineEntity",
		      "AD4": {
		        "SchemaName": "Activity",
		        "TimelineEntityValues": [
		          {
		            "UId": "timeline-1",
		            "TypeColumnValue": "type-1",
		            "OwnerColumn": "CreatedBy",
		            "SortColumn": "ModifiedOn",
		            "ColumnLayouts": [
		              {
		                "ColumnName": "CreatedOn",
		                "ColumnLayout": "{\"column\":1}"
		              },
		              {
		                "ColumnName": "Title",
		                "ColumnLayout": null
		              },
		              {
		                "ColumnName": "Body",
		                "ColumnLayout": null
		              }
		            ]
		          }
		        ]
		      }
		    }
		  }
		}
		""";

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));

		var timelineValue = GetSingleTimelineEntityValue(result.MergedContent!);
		var columnLayouts = timelineValue["ColumnLayouts"]!.AsArray();

		Assert.That(result.Status, Is.EqualTo(global::Creatio.ConflictResolver.MergeStatus.Resolved), result.ErrorMessage);
		Assert.That(result.Report.VerificationPassed, Is.True);
		Assert.That(result.Report.TrueConflicts, Is.Empty);
		Assert.That(GetStringValue(timelineValue, "OwnerColumn"), Is.EqualTo("LocalOwner"));
		Assert.That(GetStringValue(timelineValue, "SortColumn"), Is.EqualTo("ModifiedOn"));
		Assert.That(GetColumnLayout(columnLayouts, "CreatedOn")["ColumnLayout"]!.GetValue<string>(), Is.EqualTo("{\"column\":1}"));
		Assert.That(GetColumnLayout(columnLayouts, "Title")["ColumnLayout"]!.GetValue<string>(), Is.EqualTo("{\"column\":2}"));
		Assert.That(GetColumnLayout(columnLayouts, "Status"), Is.Not.Null);
		Assert.That(GetColumnLayout(columnLayouts, "Body"), Is.Not.Null);
		Assert.That(columnLayouts.OfType<JsonObject>().Any(static x => x.ContainsKey("UId")), Is.False);
	}

	[Test]
	public void MetadataMerge_FixtureCase4NewFormat_MergesByUidAndColumnName()
	{
		var baseContent = ReadFixtureFile("Case4NewFormat", "metadata-base.json");
		var localContent = ReadFixtureFile("Case4NewFormat", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case4NewFormat", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case4NewFormat", "merge-expected.json");

		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expectedContent));
	}
	
	[Test]
	public void MetadataMerge_FixtureCase4NewFormatNewMode()
	{
		var baseContent = ReadFixtureFile("Case4NewFormatNewMode", "metadata-base.json");
		var localContent = ReadFixtureFile("Case4NewFormatNewMode", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case4NewFormatNewMode", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case4NewFormatNewMode", "merge-expected.json");
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			MergeMode.Automerge));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expectedContent));
	}

	[Test]
	public void MetadataMerge_FixtureCase5_ConflictsInCollections() {
		var baseContent = ReadFixtureFile("Case5", "metadata-base.json");
		var localContent = ReadFixtureFile("Case5", "metadata-local.json");
		var remoteContent = ReadFixtureFile("Case5", "metadata-remote.json");
		var expectedContent = ReadFixtureFile("Case5", "merge-expected.json");
		var result = ResolverTestSupport.Resolver.Resolve(new global::Creatio.ConflictResolver.MergeRequest(
			global::Creatio.ConflictResolver.ConflictFileType.MetadataJson,
			baseContent,
			localContent,
			remoteContent,
			null,
			MergeMode.Automerge));
		Assert.That(
			result.MergedContent!,
			Is.EqualTo(expectedContent));
	}

	private static string ReadFixtureFile(string caseName, string fileName)
	{
		var projectRoot = GetTestProjectRootDirectory();
		var path = Path.Combine(
			projectRoot,
			"Fixtures",
			"MetadataTimelineEntity",
			caseName,
			fileName);

		return File.ReadAllText(path);
	}

	private static string NormalizeFixtureContent(string value)
	{
		return ResolverTestSupport.NormalizeLineEndings(value).TrimEnd('\r', '\n');
	}

	private static string GetTestProjectRootDirectory()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null)
		{
			var projectFilePath = Path.Combine(directory.FullName, "Creatio.ConflictResolver.Tests.csproj");
			if (File.Exists(projectFilePath))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException("Unable to locate test project directory.");
	}

	private static JsonObject GetSchema(string mergedContent)
	{
		return JsonNode.Parse(mergedContent)!.AsObject()["MetaData"]!.AsObject()["Schema"]!.AsObject();
	}

	private static JsonObject GetSingleTimelineEntityValue(string mergedContent)
	{
		var values = GetSchema(mergedContent)["AD4"]!.AsObject()["TimelineEntityValues"]!.AsArray();
		return values.OfType<JsonObject>().Single();
	}

	private static string? GetStringValue(JsonObject obj, string propertyName)
	{
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
		{
			return null;
		}

		return jsonValue.TryGetValue<string>(out var value) ? value : null;
	}

	private static JsonObject GetColumnLayout(JsonArray columnLayouts, string columnName)
	{
		return columnLayouts
			.OfType<JsonObject>()
			.Single(x => string.Equals(GetStringValue(x, "ColumnName"), columnName, StringComparison.Ordinal));
	}
}
