using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Developer-local end-to-end proof that a Color column survives the DB-first data-binding tools.
/// </summary>
/// <remarks>
/// This is a destructive lifecycle fixture, so it is marked the way <c>clio.mcp.e2e/AGENTS.md</c>
/// requires and never runs in an automatic lane. <c>create-entity-schema</c> publishes
/// configuration and then starts the asynchronous, global OData rebuild, which returns before the
/// rebuild finishes. Any test running concurrently on the same stand - in this suite or in another
/// PR's run against the same environment - then gets
/// "Creatio is currently rebuilding the OData library" instead of its own result. Neither
/// <see cref="NonParallelizableAttribute"/> nor the fixture's package cleanup bounds that
/// background rebuild, which is why the scenario is pulled out of
/// <see cref="DataBindingDbToolE2ETests"/> rather than merely serialized inside it.
/// The Color mapping itself is covered off-stand by the unit tests named in
/// <c>McpFixturePolicyTests.ColorDataBindingContract_ShouldStayCoveredOffStand_WhenExplicitFixtureNeverRunsInCi</c>.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Publishes a schema and triggers the global OData rebuild on the shared stand; run it by hand against a leased sandbox.")]
[AllureNUnit]
[AllureFeature("data-binding-db")]
[NonParallelizable]
public sealed class DataBindingDbColorSchemaE2ETests : DataBindingDbFixtureBase {

	[Test]
	[Description("Round-trips a Color column through the DB-first data-binding tools on a real Creatio environment: creates a schema with a UsrColor column, writes #009DE3 with create-data-binding-db, updates it with upsert-data-binding-row-db, reads it back with read-data-binding-db, and proves the local create-data-binding descriptor keeps the native Color data-value-type UId.")]
	[AllureTag(CreateDbToolName)]
	[AllureTag(UpsertRowDbToolName)]
	[AllureTag(ReadDbToolName)]
	[AllureName("DB-first data binding round-trips a Color column and keeps the Color data-value-type UId")]
	[AllureDescription("Creates a sandbox entity schema carrying a Color (dataValueType 18) column through create-entity-schema, writes and updates a hex Color value through the DB-first binding tools against a reachable Creatio sandbox, reads the row back, and asserts the local create-data-binding descriptor.json records the native Color data-value-type UId while data.json carries the hex value. The pre-existing DB-first scenarios all bind Lookup or Account, so none of them exercises a Color column end to end.")]
	public async Task DataBindingDb_Should_RoundTrip_Color_Column_And_Keep_Color_DataValueType() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"create-entity-schema publishes configuration and starts the global OData rebuild, which "
			+ "makes every concurrent test on the shared stand fail with \"Creatio is currently "
			+ "rebuilding the OData library\". Run this scenario by hand against a leased sandbox.");
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string schemaName = $"Usr{System.Guid.NewGuid():N}";
		string bindingName = schemaName;
		const string nameColumnName = "UsrName";
		const string colorColumnName = "UsrColor";
		const string colorValue = "#009DE3";
		const string updatedColorValue = "#FF6900";
		//The native Creatio Color data-value-type. A binding descriptor that loses it ships the
		//column as something else and the value stops round-tripping.
		const string colorDataValueTypeUId = "dafb71f9-ee9f-4e0b-a4d7-37aa15987155";
		string rowName = $"E2E Color {arrangeContext.PackageName}";
		//One explicit Id used by BOTH writes. Without it UpsertRow calls EnsureRowId, mints a fresh GUID
		//and takes the INSERT branch, so the scenario would add a second row and still pass merely because
		//the read output happens to contain the new hex value.
		string rowId = System.Guid.NewGuid().ToString();

		CommandExecutionActResult createSchemaResult = await ActCommandAsync(
			arrangeContext,
			CreateEntitySchemaToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = schemaName,
				//create-entity-schema requires a schema-level title-localizations with a non-empty en-US
				//value; without it the tool refuses and no schema is created.
				["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Color round trip" },
				["columns"] = new[] {
					new Dictionary<string, object?> {
						["column-name"] = nameColumnName,
						["type"] = "Text"
					},
					new Dictionary<string, object?> {
						["column-name"] = colorColumnName,
						["type"] = "Color"
					}
				}
			});
		AssertToolCallSucceeded(createSchemaResult);
		AssertCommandExitCode(createSchemaResult, 0,
			"the schema carrying the Color column must exist before any binding can reference it");

		//create-entity-schema returns while the global OData rebuild it started is still running, so
		//the binding call below can hit "Creatio is currently rebuilding the OData library" instead of
		//the Color column. Prove the new schema answers over OData first, otherwise the only real-
		//process evidence for type 18 is decided by that race.
		await WaitUntilSchemaIsQueryableAsync(arrangeContext, schemaName);

		// Act - write the Color value through the DB-first binding
		CommandExecutionActResult createBindingResult = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = schemaName,
				["binding-name"] = bindingName,
				["rows"] =
					$"[{{\"values\":{{\"Id\":\"{rowId}\",\"{nameColumnName}\":\"{rowName}\","
					+ $"\"{colorColumnName}\":\"{colorValue}\"}}}}]"
			});

		// Act - read the binding back so the stored Color value is observed over the wire
		CommandExecutionActResult readResult = await ActCommandAsync(
			arrangeContext,
			ReadDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = bindingName
			});

		// Act - the local file-based path, whose descriptor is where the data-value-type UId lands
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			arrangeContext.Settings,
			[
				"create-data-binding",
				"--package", arrangeContext.PackageName,
				"--schema", schemaName,
				"--binding-name", bindingName,
				"--values", $"{{\"{nameColumnName}\":\"{rowName}\",\"{colorColumnName}\":\"{colorValue}\"}}",
				"-e", arrangeContext.EnvironmentName!
			],
			workingDirectory: arrangeContext.WorkspacePath,
			cancellationToken: arrangeContext.CancellationTokenSource.Token);
		string bindingDirectoryPath = Path.Combine(
			arrangeContext.WorkspacePath, "packages", arrangeContext.PackageName, "Data", bindingName);
		string descriptorPath = Path.Combine(bindingDirectoryPath, "descriptor.json");
		string dataPath = Path.Combine(bindingDirectoryPath, "data.json");

		// Assert
		AssertToolCallSucceeded(createBindingResult);
		AssertCommandExitCode(createBindingResult, 0,
			"create-data-binding-db must accept a Color column value; a Color mapped to no CLR type fails serialization");
		AssertToolCallSucceeded(readResult);
		AssertCommandExitCode(readResult, 0,
			"read-data-binding-db must project a binding that contains a Color column");
		AssertOutputContains(readResult, colorValue,
			"the stored Color value must come back from the remote binding unchanged");

		File.Exists(descriptorPath).Should().BeTrue(
			because: $"create-data-binding must write the binding descriptor to {descriptorPath}");
		File.Exists(dataPath).Should().BeTrue(
			because: $"create-data-binding must write the binding data to {dataPath}");
		File.ReadAllText(descriptorPath).Should().ContainEquivalentOf(colorDataValueTypeUId,
			because: "the descriptor column for a Color column must record the native Color data-value-type UId");
		File.ReadAllText(dataPath).Should().Contain(colorValue,
			because: "the local artifact must carry the hex Color value it was given");

		// Act - update the same row through the upsert tool and read it back once more
		CommandExecutionActResult upsertResult = await ActCommandAsync(
			arrangeContext,
			UpsertRowDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = bindingName,
				["values"] =
					$"{{\"Id\":\"{rowId}\",\"{nameColumnName}\":\"{rowName}\","
					+ $"\"{colorColumnName}\":\"{updatedColorValue}\"}}"
			});
		CommandExecutionActResult readAfterUpsertResult = await ActCommandAsync(
			arrangeContext,
			ReadDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = bindingName
			});

		// Assert
		AssertToolCallSucceeded(upsertResult);
		AssertCommandExitCode(upsertResult, 0,
			"upsert-data-binding-row-db must be able to write a Color column too, not only the create path");
		AssertToolCallSucceeded(readAfterUpsertResult);
		AssertCommandExitCode(readAfterUpsertResult, 0,
			"the read that proves the update landed has to have succeeded before its output means anything");
		AssertOutputContains(readAfterUpsertResult, updatedColorValue,
			"the upserted Color value must be the one the binding reports afterwards");
		AssertOutputDoesNotContain(readAfterUpsertResult, colorValue,
			"reusing the row Id makes the upsert an UPDATE; if the original hex is still there the tool "
			+ "inserted a second row instead");
		CountOutputOccurrences(readAfterUpsertResult, rowId).Should().Be(1,
			because: "the binding must still hold exactly one row - the one the create wrote and the "
				+ "upsert updated in place");
	}

}
