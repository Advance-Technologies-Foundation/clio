namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Covers the read-only pre-flight that turns Creatio's prose refusals into fields an agent can branch on.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
internal class PackageDeletionPreflightTests : BaseClioModuleTests {

	private const string PackageName = "UsrScratchPkg";
	private const string PackageUId = "6f9619ff-8b86-d011-b42d-00c05fc76f10";
	private const string EnvironmentMaintainer = "Customer";

	private IApplicationClient _applicationClient = null!;
	private PackageDeletionPreflight _preflight = null!;

	// Per-rootSchema answers, keyed by the Select query's rootSchemaName.
	private readonly Dictionary<string, string> _selectAnswers = new(StringComparer.Ordinal);

	public override void Setup() {
		base.Setup();
		// NUnit 4 reuses one fixture instance per fixture, so per-test state must be reset here.
		_selectAnswers.Clear();
		// The environment's own maintainer, the value the foreign rule compares a package's maintainer against.
		_selectAnswers["SysSettingsValue"] = Answer(new { TextValue = EnvironmentMaintainer });
		_preflight = Container.GetRequiredService<IPackageDeletionPreflight>() as PackageDeletionPreflight
			?? throw new InvalidOperationException("The container must resolve the concrete pre-flight.");
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => SelectAnswer(callInfo.ArgAt<string>(1)));

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select)
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");

		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	private string SelectAnswer(string body) {
		using JsonDocument document = JsonDocument.Parse(body);
		string rootSchema = document.RootElement.GetProperty("rootSchemaName").GetString() ?? string.Empty;
		return _selectAnswers.TryGetValue(rootSchema, out string answer)
			? answer
			: Answer(rows: []);
	}

	private static string Answer(params object[] rows) =>
		"""{"success":true,"rows":""" + JsonSerializer.Serialize(rows) + "}";

	private static object PackageRow(int? installType = 0, string? maintainer = EnvironmentMaintainer) =>
		new { Name = PackageName, UId = PackageUId, Maintainer = maintainer, InstallType = installType };

	// The dependency read projects the dependent package through the SysPackage lookup, so the row key is the
	// dotted column path, not a bare Name.
	private static string DependencyAnswer(params string[] dependentNames) =>
		Answer(dependentNames.Select(name => new Dictionary<string, string> { ["SysPackage.Name"] = name }).ToArray());

	[Test]
	[Description("Reports Ready and the package inventory when the package is editable and nothing depends on it.")]
	public void Assess_Should_Return_Ready_When_Package_Is_Editable_And_Nothing_Depends_On_It() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = Answer();
		_selectAnswers["SysSchema"] = Answer(new { Name = "UsrOrder" }, new { Name = "UsrCustomer" });
		_selectAnswers["SysPackageSchemaData"] = Answer(new { Name = "UsrOrder_List" });
		_selectAnswers["SysPackageSqlScript"] = Answer(new { Name = "001_Init" });

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Ready,
			because: "an editable package with no dependents is the only case the pre-flight may approve");
		assessment.CanDelete.Should().BeTrue(because: "Ready is the status that authorizes the delete");
		assessment.PackageUId.Should().Be(PackageUId, because: "the caller needs the identifier it is about to lose");
		assessment.Warning.Should().BeNull(because: "every read succeeded");
		assessment.Error.Should().BeNull(because: "no read failed");
		assessment.Inventory.Should().NotBeNull(because: "the report is read before the delete, where the rows still exist");
		assessment.Inventory!.SchemaCount.Should().Be(2, because: "two schemas were listed");
		assessment.Inventory.Schemas.Should().ContainInOrder(["UsrCustomer", "UsrOrder"],
			because: "the listed names are ordered for a stable report");
		assessment.Inventory.DataBindingCount.Should().Be(1, because: "one data binding was listed");
		assessment.Inventory.SqlScriptCount.Should().Be(1, because: "one SQL script was listed");
		assessment.Inventory.Partial.Should().BeFalse(because: "every content read succeeded");
	}

	[Test]
	[Description("Reports NotFound when no SysPackage row matches the name, so the caller learns the name is wrong before any delete.")]
	public void Assess_Should_Return_NotFound_When_No_Package_Row_Matches() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer();

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.NotFound,
			because: "deleting a name that is not installed would only burn a server call to learn that");
		assessment.CanDelete.Should().BeFalse(because: "there is nothing to delete");
		assessment.PackageUId.Should().BeNull(because: "the row was never read");
	}

	[Test]
	[Description("Reports Locked when the package's maintainer differs from the environment's, the first half of the platform's own foreign rule.")]
	public void Assess_Should_Return_Locked_When_The_Maintainer_Differs_From_The_Environments() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow(maintainer: "Partner"));

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Locked,
			because: "a package another maintainer owns is not this environment's scratch package to delete");
		assessment.CanDelete.Should().BeFalse(because: "the foreign rule is a refusal the pre-flight must surface, not send");
	}

	[Test]
	[Description("Compares maintainers exactly like the platform does — a case difference is a different maintainer.")]
	public void Assess_Should_Return_Locked_When_Only_The_Maintainer_Case_Differs() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow(maintainer: "customer"));

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Locked,
			because: "the platform compares maintainers case-sensitively, so the pre-flight must not be kinder");
	}

	[Test]
	[Description("Reports Unavailable when the maintainer setting read fails, because the foreign rule was only half evaluated.")]
	public void Assess_Should_Return_Unavailable_When_The_Maintainer_Setting_Read_Fails() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysSettingsValue"] = """{"success":false,"errorInfo":{"message":"route table reload"}}""";

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Unavailable,
			because: "a package whose maintainer was never compared may be foreign, and a delete may not run blind");
		assessment.CanDelete.Should().BeFalse(because: "the pre-flight refused to approve a deletion it could not check");
	}

	[Test]
	[Description("Still reports Ready when the environment has no maintainer set, so the install type alone decides.")]
	public void Assess_Should_Return_Ready_When_The_Environment_Has_No_Maintainer_Set() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow(maintainer: "Customer"));
		_selectAnswers["SysSettingsValue"] = Answer();
		_selectAnswers["SysPackageDependency"] = Answer();

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Ready,
			because: "with no environment maintainer the maintainer half of the foreign rule has nothing to compare against");
	}

	[Test]
	[Description("Reports Locked when the InstallType is not the editable one, the other signal PackageTargetResolver uses.")]
	public void Assess_Should_Return_Locked_When_InstallType_Is_Not_Editable() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow(installType: 1));

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Locked,
			because: "an InstallType other than SourceControl(0) is a repository package the environment installed");
		assessment.CanDelete.Should().BeFalse(because: "the environment will not delete a package it did not create");
	}

	[Test]
	[Description("Reports BlockedByDependents and names the dependent packages when SysPackageDependency has rows for it.")]
	public void Assess_Should_Return_BlockedByDependents_When_Other_Packages_Depend_On_It() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = DependencyAnswer("UsrChildA", "UsrChildB", "UsrChildA");

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.BlockedByDependents,
			because: "deleting a package its dependents ship against would break them");
		assessment.CanDelete.Should().BeFalse(because: "the dependents must be moved or deleted first");
		assessment.Dependents.Should().ContainInOrder(["UsrChildA", "UsrChildB"],
			because: "the dependents are de-duplicated and ordered so the caller sees exactly what to unblock");
	}

	[Test]
	[Description("Reports Unavailable when the SysPackage read fails, because a deletion whose state was never read is not a safe deletion.")]
	public void Assess_Should_Return_Unavailable_When_The_Package_Read_Fails() {
		// Arrange
		_selectAnswers["SysPackage"] = """{"success":false,"errorInfo":{"message":"service down"}}""";

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Unavailable,
			because: "a failed read must not be read as Ready, or the delete would run blind");
		assessment.CanDelete.Should().BeFalse(because: "the pre-flight refused to approve a deletion it could not check");
		assessment.Error.Should().NotBeNull(because: "the caller needs to know a read failed");
	}

	[Test]
	[Description("Reports Unavailable when the dependency read fails, because dependents that were never read may still exist.")]
	public void Assess_Should_Return_Unavailable_When_The_Dependency_Read_Fails() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = """{"success":false,"errorInfo":{"message":"route table reload"}}""";

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Unavailable,
			because: "the dependency read is the one that decides, and it never succeeded");
		assessment.CanDelete.Should().BeFalse(because: "an unread dependent set is not an empty one");
	}

	[Test]
	[Description("Still reports Ready when a content read fails, with the inventory marked partial so a zero count reads as not read.")]
	public void Assess_Should_Return_Ready_With_Partial_Inventory_When_A_Content_Read_Fails() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = Answer();
		_selectAnswers["SysSchema"] = Answer(new { Name = "UsrOrder" });
		_selectAnswers["SysPackageSchemaData"] = """{"success":false,"errorInfo":{"message":"column absent"}}""";

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Ready,
			because: "the content reads only report; a package the environment considers deletable is not kept alive by a failed report");
		assessment.Inventory.Should().NotBeNull(because: "what was read is still reported");
		assessment.Inventory!.Partial.Should().BeTrue(because: "one of the content reads failed");
		assessment.Warning.Should().NotBeNull(because: "the caller must learn the inventory is partial, not complete");
		assessment.Inventory.SchemaCount.Should().Be(1, because: "the schemas read before the failure is kept");
		assessment.Inventory.DataBindingCount.Should().Be(0,
			because: "the failed read contributes no rows and the partial flag says so");
	}

	[Test]
	[Description("Caps the listed names at the reporting limit while keeping the true count, so a large package still reads as large.")]
	public void Assess_Should_Cap_Listed_Names_But_Keep_The_Count() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = DependencyAnswer();
		object[] rows = Enumerable.Range(1, 30).Select(i => new { Name = $"UsrSchema{i:D2}" }).ToArray();
		_selectAnswers["SysSchema"] = Answer(rows);

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName);

		// Assert
		assessment.Inventory!.SchemaCount.Should().Be(30,
			because: "the count is the number of rows read, never the length of the list");
		assessment.Inventory.Schemas.Should().HaveCount(25,
			because: "the list is capped so the report stays bounded");
	}

	[Test]
	[Description("Reports Unavailable for an empty name, because an empty filter would read every package.")]
	public void Assess_Should_Return_Unavailable_When_The_Name_Is_Empty() {
		// Act
		PackageDeletionAssessment assessment = _preflight.Assess("  ");

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Unavailable,
			because: "an empty name is a caller error the pre-flight refuses rather than widening to a whole-table read");
		assessment.CanDelete.Should().BeFalse(because: "no package was named, so none may be deleted");
	}

	[Test]
	[Description("Finds the package case-insensitively, matching how Creatio stores and lists package names.")]
	public void Assess_Should_Match_The_Package_Name_Case_Insensitively() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());

		// Act
		PackageDeletionAssessment assessment = _preflight.Assess(PackageName.ToUpperInvariant());

		// Assert
		assessment.Status.Should().Be(PackageDeletionStatus.Ready,
			because: "the caller may spell the name in a different case than the environment stores it");
	}

	[Test]
	[Description("Sends the dependency read filtered on DependOnPackage.Name so it lists packages that depend on the target, not the target's own dependencies.")]
	public void Assess_Should_Filter_The_Dependency_Read_On_The_Target() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = DependencyAnswer();

		// Act
		_preflight.Assess(PackageName);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body =>
				body.Contains("\"rootSchemaName\":\"SysPackageDependency\"")
				&& body.Contains("\"columnPath\":\"DependOnPackage.Name\"")
				&& body.Contains($"\"value\":\"{PackageName}\"")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns false from the read-back when the package is gone, the outcome the delete promise is verified against.")]
	public void ProbeExists_Should_Return_False_When_The_Package_Is_Gone() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer();

		// Act
		bool? exists = _preflight.ProbeExists(PackageName);

		// Assert
		exists.Should().BeFalse(because: "no SysPackage row means the deletion is confirmed");
	}

	[Test]
	[Description("Returns true from the read-back when the package is still listed, so a lying delete call is reported as unconfirmed.")]
	public void ProbeExists_Should_Return_True_When_The_Package_Is_Still_Listed() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());

		// Act
		bool? exists = _preflight.ProbeExists(PackageName);

		// Assert
		exists.Should().BeTrue(because: "the row is still there, so the absence is not confirmed");
	}

	[Test]
	[Description("Returns null from the read-back when the environment could not be asked, which the caller reports as unverified, not deleted.")]
	public void ProbeExists_Should_Return_Null_When_The_Read_Fails() {
		// Arrange
		_selectAnswers["SysPackage"] = """{"success":false,"errorInfo":{"message":"connection dropped"}}""";

		// Act
		bool? exists = _preflight.ProbeExists(PackageName);

		// Assert
		exists.Should().BeNull(because: "a failed verification read proves nothing, so it is reported as unknown");
	}
}
