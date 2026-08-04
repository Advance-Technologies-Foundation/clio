using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.Branding;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <see cref="IPackageDataBinder"/> over a remote environment: package resolution
/// (including the CurrentPackageId fallback), the SysSettingsValue natural-key / force-update delivery policy
/// (the correctness core — an Id-keyed binding would insert a duplicate default row on any target that
/// already has the setting), the confirmed-off verification of a feature state, the schema guards that keep a
/// delivery from overwriting or deleting a binding it does not own, and the guarantee that delivering package
/// data never touches the environment's runtime rows.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class EnvironmentPackageDataBinderTests {

	private const string PackageName = "UsrBrandingPkg";
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid AllUsersAdminUnitId = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid BackgroundImageId = Guid.Parse("7a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid ExistingBindingUId = Guid.Parse("9a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

	/// <summary>The SysPackage row id the environment's CurrentPackageId setting points at.</summary>
	private static readonly Guid CurrentPackageRowId = Guid.Parse("2e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a6b");

	private static readonly Guid PanelIconFeatureId = Guid.Parse("6b1c2d3e-4f50-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid PanelIconFeatureStateRowId = Guid.Parse("7c1d2e3f-4a50-4b6c-8d9e-0f1a2b3c4d5e");

	private const string LoginLogoCode = "LogoImage";
	private const string LoginLogoFolder = "SysSettingsValue_LogoImage";
	private const string ConfigCode = "CrtBackgroundConfig";
	private const string ConfigFolder = "SysSettingsValue_CrtBackgroundConfig";
	private const string ConfigDefFolder = "SysSettings_CrtBackgroundConfig";
	private const string ImageFolder = "SysImage_ShellBackground";
	private const string FeatureCode = "UsePanelIconBackground";
	private const string FeatureStateFolder = "AdminUnitFeatureState_UsePanelIconBackground";
	private const string FeatureDefFolder = "Feature_UsePanelIconBackground";

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];

	/// <summary>Every logo slot code plus the splash toggle — the full folder set of a four-slot set-logo run.</summary>
	private static readonly string[] AllLogoCodes = [
		"LogoImage", "MenuLogoImage", "ConfigurationPageLogoImage", "CrtAppToolbarLogo",
		"HideSplashScreenLogoImage"
	];

	private const string SelectUrl = "http://localhost/0/DataService/json/SyncReply/SelectQuery";
	private const string SaveSchemaUrl = "http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema";
	private const string DeleteBindingUrl = "http://localhost/0/DataService/json/SyncReply/DeletePackageSchemaDataRequest";
	private const string DeleteRowUrl = "http://localhost/0/DataService/json/SyncReply/DeleteQuery";
	private const string InsertRowUrl = "http://localhost/0/DataService/json/SyncReply/InsertQuery";
	private const string UpdateRowUrl = "http://localhost/0/DataService/json/SyncReply/UpdateQuery";

	#region Tests: package resolution

	[Test]
	[Category("Unit")]
	[Description("Resolves the package the environment's CurrentPackageId system setting points at when the caller names none.")]
	public void UsePackage_Should_Resolve_The_CurrentPackageId_Package_When_None_Is_Named() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = environment.CreateBinder();

		// Act
		string package = sut.UsePackage(null);

		// Assert
		package.Should().Be(PackageName,
			because: "design-time writes land in the environment's current package, so package-data delivery follows the same convention instead of a hardcoded well-known package name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the resolved package name so the caller can name it to the user in the run summary.")]
	public void UsePackage_Should_Return_The_Named_Package() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = environment.CreateBinder();

		// Act
		string package = sut.UsePackage(PackageName);

		// Assert
		package.Should().Be(PackageName,
			because: "the run summary names the package the data was delivered into");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops with an actionable error when no package is named and CurrentPackageId is unset, instead of silently picking a well-known package.")]
	public void UsePackage_Should_Throw_When_No_Package_Is_Named_And_CurrentPackageId_Is_Unset() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.CurrentPackageIdValue = string.Empty;
		IPackageDataBinder sut = environment.CreateBinder();

		// Act
		Action act = () => sut.UsePackage(null);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "guessing a package would deliver data somewhere the user never chose, so the run must stop and ask for one")
			.WithMessage("*CurrentPackageId*");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops with an actionable error when CurrentPackageId points at a package that cannot be resolved on the environment.")]
	public void UsePackage_Should_Throw_When_CurrentPackageId_Does_Not_Resolve() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.CurrentPackageIdValue = "3f2a1b0c-4d5e-4f60-8a9b-0c1d2e3f4a5b";
		IPackageDataBinder sut = environment.CreateBinder();

		// Act
		Action act = () => sut.UsePackage(null);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a dangling current-package id is a configuration problem the user has to see, not a reason to fall back to another package")
			.WithMessage("*CurrentPackageId*");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops naming the list-packages command when the named package does not exist on the environment.")]
	public void UsePackage_Should_Throw_When_The_Package_Is_Not_Found() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = environment.CreateBinder();

		// Act
		Action act = () => sut.UsePackage("NoSuchPackage");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the user needs a next step, not just the fact that the name did not resolve")
			.WithMessage("*list-packages*");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to deliver before a package was selected, naming the missing call instead of failing obscurely.")]
	public void Deliver_Should_Throw_When_No_Package_Was_Selected() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = environment.CreateBinder();

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the caller must be told which call it forgot rather than get an obscure failure")
			.WithMessage("*UsePackage*");
	}

	#endregion

	#region Tests: sys-setting value delivery — natural-key policy

	[Test]
	[Category("Unit")]
	[Description("Saves the setting's All-Users value row into the named folder against the SysSettingsValue entity.")]
	public void BindSysSettingsValue_Should_Save_The_Value_Row_Into_The_Folder() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		outcome.Bound.Should().BeTrue(because: "the setting has an All-Users value row to deliver");
		string saveBody = environment.SingleSaveBodyFor(LoginLogoFolder);
		ReadJsonString(saveBody, "entitySchemaName").Should().Be("SysSettingsValue",
			because: "a sys-setting value is delivered as the All-Users SysSettingsValue row of the setting");
		ReadJsonArray(saveBody, "boundRecordIds").Should().BeEquivalentTo([environment.ValueRowIdOf(LoginLogoCode)],
			because: "the folder must deliver exactly that setting's All-Users value row");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the SysSettingsValue binding on the setting's natural key, because the value row's Id differs per environment.")]
	public void BindSysSettingsValue_Should_Key_The_Binding_On_The_Natural_Key() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor(LoginLogoFolder))
			.Should().BeEquivalentTo(["SysSettings", "SysAdminUnit"],
				because: "install must match the target's own All-Users row of the same setting instead of inserting a duplicate default row");
	}

	[Test]
	[Category("Unit")]
	[Description("Never keys the SysSettingsValue binding on Id — an Id key can only insert, because the id exists on no other environment.")]
	public void BindSysSettingsValue_Should_Not_Key_The_Binding_On_Id() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor(LoginLogoFolder)).Should().NotContain("Id",
			because: "an Id-keyed binding would never match the target's existing row and would insert a duplicate");
	}

	[Test]
	[Category("Unit")]
	[Description("Force-updates every value column so installing the package overwrites the matched row's value.")]
	public void BindSysSettingsValue_Should_ForceUpdate_The_Value_Columns() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		string[] forced = ForceUpdateColumns(environment.SingleSaveBodyFor(LoginLogoFolder));
		forced.Should().Contain("BinaryValue")
			.And.Contain("TextValue",
				because: "matching the target's row is pointless unless the delivered value actually overwrites it");
		forced.Should().NotContain("SysSettings")
			.And.NotContain("SysAdminUnit",
				because: "a key column is a match column, so force-updating it would be contradictory");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a setting without an All-Users value as a warning instead of failing or silently skipping.")]
	public void BindSysSettingsValue_Should_Report_A_Setting_Without_AllUsers_Value_As_A_Warning() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RemoveAllUsersValue(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		outcome.Bound.Should().BeFalse(because: "there is no row to deliver");
		outcome.Warnings.Should().ContainSingle(warning => warning.Contains("no All-Users value"),
			because: "the user has to learn why the setting was left out rather than find a silent gap");
		environment.SavedBindingNames().Should().NotContain(LoginLogoFolder,
			because: "nothing may be saved for a setting that has no deliverable row");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops a previously shipped folder when the setting's row is gone, and surfaces the removal.")]
	public void BindSysSettingsValue_Should_Drop_A_Previously_Shipped_Folder_When_The_Row_Is_Gone() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId);
		environment.RemoveAllUsersValue(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		environment.DeletedBindingNames().Should().Contain(LoginLogoFolder,
			because: "the package must stop shipping a snapshot the environment no longer backs");
		outcome.Warnings.Should().Contain(warning => warning.Contains("previously shipped binding removed"),
			because: "reconciling away a shipped folder is a change the user must see, not a silent side effect");
	}

	[Test]
	[Category("Unit")]
	[Description("Reuses the existing binding's UId when refreshing, so a re-run updates the folder in place instead of registering a duplicate.")]
	public void BindSysSettingsValue_Should_Reuse_The_Existing_Binding_UId_When_Refreshing() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		ReadJsonString(environment.SingleSaveBodyFor(LoginLogoFolder), "uId")
			.Should().Be(ExistingBindingUId.ToString(),
				because: "re-running must refresh the folder the package already ships, not add a second one");
	}

	[Test]
	[Category("Unit")]
	[Description("Creates the binding under a fresh UId when the package does not ship the folder yet.")]
	public void BindSysSettingsValue_Should_Create_The_Binding_Under_A_Fresh_UId_When_None_Exists() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		string uId = ReadJsonString(environment.SingleSaveBodyFor(LoginLogoFolder), "uId");
		Guid.TryParse(uId, out Guid parsed).Should().BeTrue(because: "a new registration needs a usable UId");
		parsed.Should().NotBe(Guid.Empty, because: "an empty UId would not identify the registration");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails loudly when the environment's SysSettingsValue schema is missing a natural-key column, instead of shipping a binding whose key silently narrowed to a wildcard match.")]
	public void BindSysSettingsValue_Should_Throw_When_A_Key_Column_Is_Missing_From_The_Schema() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.DropSchemaColumn("SysSettingsValue", "SysAdminUnit");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*SysAdminUnit*",
				because: "a key that narrowed to SysSettings alone would match and force-update every value row of the setting on the target, personal overrides included, so the run must stop and name the missing column");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails loudly when a delivered value column is missing from the schema, instead of shipping a binding with an incomplete snapshot.")]
	public void BindSysSettingsValue_Should_Throw_When_A_Value_Column_Is_Missing_From_The_Schema() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.DropSchemaColumn("SysSettingsValue", "BinaryValue");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*BinaryValue*",
				because: "a logo lives in BinaryValue, and a binding without that column would install an empty snapshot");
	}

	[Test]
	[Category("Unit")]
	[Description("Saves nothing at all when the schema projection is incomplete — a partial delivery is worse than none.")]
	public void BindSysSettingsValue_Should_Not_Save_Anything_When_The_Projection_Is_Incomplete() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.DropSchemaColumn("SysSettingsValue", "SysAdminUnit");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		try {
			sut.BindSysSettingsValue(LoginLogoCode);
		} catch (InvalidOperationException) {
			// The throw itself is asserted elsewhere; this test pins the absence of side effects.
		}

		// Assert
		environment.SaveBodies.Should().BeEmpty(
			because: "a delivery the run refused must leave the package exactly as it was");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to deliver a setting defined as the secret-bearing SecureText type.")]
	public void BindSysSettingsValue_Should_Refuse_A_SecureText_Setting() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.SetSettingValueType(LoginLogoCode, "SecureText");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		outcome.Bound.Should().BeFalse(
			because: "a secret value must never travel inside a package, whichever caller asked for it");
		environment.SavedBindingNames().Should().NotContain(LoginLogoFolder,
			because: "no snapshot of a secret-bearing setting may be saved");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the SecureText refusal as a warning that names the setting and the reason.")]
	public void BindSysSettingsValue_Should_Report_A_SecureText_Setting_As_A_Warning() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.SetSettingValueType(LoginLogoCode, "SecureText");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		outcome.Warnings.Should().Contain(
			warning => warning.Contains(LoginLogoCode) && warning.Contains("SecureText"),
			because: "the user has to learn why the setting was left out instead of finding a silent gap");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an undefined setting as its own cause, distinct from a defined setting without an All-Users value.")]
	public void BindSysSettingsValue_Should_Report_An_Undefined_Setting_As_Its_Own_Cause() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RemoveSettingDefinition(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		outcome.Warnings.Should().Contain(warning => warning.Contains("not defined"),
			because: "an undefined setting and a missing value are different problems with different fixes, and the report must not blur them");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops when a setting code resolves to two definition rows, so the package cannot deliver a row the user never chose.")]
	public void BindSysSettingsValue_Should_Throw_When_A_Setting_Has_Duplicate_Definitions() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.DuplicateSettingDefinition(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*multiple definitions*",
				because: "a first-wins pick would package a row the caller never chose");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a rejected SaveSchema instead of reporting the folder as delivered.")]
	public void BindSysSettingsValue_Should_Throw_When_SaveSchema_Is_Rejected() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.SaveSchemaResponse = """{"success":false,"errorInfo":{"message":"package is locked"}}""";
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*package is locked*",
				because: "a save the platform refused must never be reported to the user as shipped");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces a rejected binding delete instead of pretending the package stopped shipping the folder.")]
	public void BindSysSettingsValue_Should_Throw_When_The_Binding_Delete_Is_Rejected() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId);
		environment.RemoveAllUsersValue(LoginLogoCode);
		environment.DeleteBindingResponse = """{"success":false,"errorInfo":{"message":"delete rejected"}}""";
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*delete rejected*",
				because: "a failed drop leaves the stale folder in the package, and the run must not report it as removed");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops when the folder name already delivers a different entity schema, instead of overwriting a binding this delivery does not own.")]
	public void BindSysSettingsValue_Should_Throw_When_An_Existing_Binding_Delivers_A_Different_Schema() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId, "Contact");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Contact*",
				because: "silently re-saving the name under another schema would destroy whatever the existing binding delivered");
	}

	[Test]
	[Category("Unit")]
	[Description("Never deletes a same-name binding that delivers a different schema, even when the delivery would otherwise drop the folder.")]
	public void BindSysSettingsValue_Should_Not_Delete_A_Foreign_Schema_Binding() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId, "Contact");
		environment.RemoveAllUsersValue(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		environment.DeleteBodies.Should().BeEmpty(
			because: "the delete endpoint keys on (package, name) alone, so deleting would destroy package data this delivery does not own");
	}

	[Test]
	[Category("Unit")]
	[Description("Warns when a foreign-schema binding blocks a drop, so the collision is visible instead of silently skipped.")]
	public void BindSysSettingsValue_Should_Warn_When_A_Foreign_Schema_Binding_Blocks_A_Drop() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId, "Contact");
		environment.RemoveAllUsersValue(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		outcome.Warnings.Should().Contain(
			warning => warning.Contains("left untouched") && warning.Contains("Contact"),
			because: "the user has to resolve the name collision themselves, and can only do that if the run names it");
		environment.DeleteBodies.Should().BeEmpty(
			because: "deleting a foreign binding would destroy package data this delivery does not own");
	}

	#endregion

	#region Tests: sys-setting value delivered together with its definition

	[Test]
	[Category("Unit")]
	[Description("Delivers the setting's definition row alongside the value when the caller ships a setting clio itself creates.")]
	public void BindSysSettingsValue_Should_Deliver_The_Definition_Alongside_The_Value_When_Asked() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(ConfigCode, includeDefinition: true);

		// Assert
		outcome.Bound.Should().BeTrue(because: "the setting has a value and a definition to deliver");
		ReadJsonString(environment.SingleSaveBodyFor(ConfigFolder), "entitySchemaName").Should().Be("SysSettingsValue",
			because: "the value folder delivers the All-Users value row");
		string defBody = environment.SingleSaveBodyFor(ConfigDefFolder);
		ReadJsonString(defBody, "entitySchemaName").Should().Be("SysSettings",
			because: "the definition folder delivers the setting's own SysSettings row");
		ReadJsonArray(defBody, "boundRecordIds").Should().BeEquivalentTo([environment.DefinitionRowIdOf(ConfigCode)],
			because: "the definition folder must deliver exactly this setting's definition row");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the definition folder on the row's own Id so the value row's setting reference stays intact on the target.")]
	public void BindSysSettingsValue_Should_Key_The_Definition_On_Its_Own_Id() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(ConfigCode, includeDefinition: true);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor(ConfigDefFolder)).Should().BeEquivalentTo(["Id"],
			because: "preserving the definition's id is what keeps the value row's reference resolvable");
	}

	[Test]
	[Category("Unit")]
	[Description("Writes the definition folder before the value folder, so a write that fails partway can never leave a value row whose setting the package does not ship.")]
	public void BindSysSettingsValue_Should_Write_The_Definition_Before_The_Value() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindSysSettingsValue(ConfigCode, includeDefinition: true);

		// Assert
		environment.SavedBindingNames().Should().ContainInOrder(new[] { ConfigDefFolder, ConfigFolder },
			because: "a value row keyed on the setting reference is unresolvable without the definition, while a definition alone resolves on its own");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves the value folder unwritten when the definition folder cannot be saved, so the package never ships the dangling half of the pair.")]
	public void BindSysSettingsValue_Should_Not_Write_The_Value_When_The_Definition_Save_Fails() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.DropSchemaColumn("SysSettings", "Description");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(ConfigCode, includeDefinition: true);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an incomplete definition projection must stop the delivery instead of shipping a partial group");
		environment.SavedBindingNames().Should().NotContain(ConfigFolder,
			because: "the value row must not reach the package once its definition could not be delivered");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops both the value and the definition folder when a grouped setting cannot be delivered — the pair stands or falls together.")]
	public void BindSysSettingsValue_Should_Drop_Both_Folders_When_A_Grouped_Setting_Cannot_Be_Delivered() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ConfigFolder, ExistingBindingUId);
		environment.RegisterExistingBinding(ConfigDefFolder, Guid.Parse("4d5e6f7a-8b9c-4d0e-9f1a-2b3c4d5e6f7a"));
		environment.SetSettingValueType(ConfigCode, "SecureText");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(ConfigCode, includeDefinition: true);

		// Assert
		environment.DeletedBindingNames().Should().Contain(ConfigFolder)
			.And.Contain(ConfigDefFolder,
				because: "a definition without its value (or the reverse) is exactly the partially shipped group the pairing exists to prevent");
		outcome.Warnings.Should()
			.Contain(warning => warning.StartsWith(ConfigFolder) && warning.Contains("previously shipped binding removed"))
			.And.Contain(warning => warning.StartsWith(ConfigDefFolder) && warning.Contains("previously shipped binding removed"),
				because: "the package now delivers less than it did, which the run summary must surface for both folders");
	}

	[Test]
	[Category("Unit")]
	[Description("Blames the shipping policy, not a missing row, when a folder leaves the package because its setting turned out to be secret-bearing.")]
	public void BindSysSettingsValue_Should_Name_ThePolicy_AsTheRemovalReason_ForASecretBearingSetting() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ConfigFolder, ExistingBindingUId);
		environment.SetSettingValueType(ConfigCode, "SecureText");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindSysSettingsValue(ConfigCode);

		// Assert
		outcome.Warnings.Should().Contain(
			warning => warning.StartsWith(ConfigFolder) && warning.Contains("must not travel in a package"),
			because: "the value row is still on the environment — only policy stops it, and the removal line must say so");
		outcome.Warnings.Should().NotContain(warning => warning.Contains("no longer has a source row"),
			because: "that cause claims the row is gone and would send anyone debugging the run looking for a deletion that never happened");
	}

	#endregion

	#region Tests: feature off-state delivery — confirmed-off verification

	[Test]
	[Category("Unit")]
	[Description("Keys the feature state binding on the feature's natural key, because the state row's Id differs per environment.")]
	public void BindFeatureOffState_Should_Key_The_State_On_The_Natural_Key() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeTrue(because: "the environment carries a confirmed All-Users off-state");
		KeyColumns(environment.SingleSaveBodyFor(FeatureStateFolder))
			.Should().BeEquivalentTo(["Feature", "SysAdminUnit"],
				because: "install must match the target's own state row of the same feature instead of inserting a duplicate");
	}

	[Test]
	[Category("Unit")]
	[Description("Force-updates the FeatureState column so installing the package actually turns the feature off on the target.")]
	public void BindFeatureOffState_Should_ForceUpdate_The_FeatureState_Column() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindFeatureOffState(FeatureCode);

		// Assert
		ForceUpdateColumns(environment.SingleSaveBodyFor(FeatureStateFolder))
			.Should().BeEquivalentTo(["FeatureState"],
				because: "matching the target's row is pointless unless the delivered off-state overwrites it");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the feature definition folder on Id so the state row's feature reference resolves on the target.")]
	public void BindFeatureOffState_Should_Key_The_Feature_Definition_On_Id() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindFeatureOffState(FeatureCode);

		// Assert
		string defBody = environment.SingleSaveBodyFor(FeatureDefFolder);
		ReadJsonString(defBody, "entitySchemaName").Should().Be("Feature",
			because: "the definition folder delivers the persisted Feature row");
		KeyColumns(defBody).Should().BeEquivalentTo(["Id"],
			because: "preserving the feature row's id keeps the state row's reference intact");
	}

	[Test]
	[Category("Unit")]
	[Description("Writes the feature definition folder before the state folder, so a write that fails partway can never leave a state row whose feature the package does not ship.")]
	public void BindFeatureOffState_Should_Write_The_Definition_Before_The_State() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.BindFeatureOffState(FeatureCode);

		// Assert
		environment.SavedBindingNames().Should().ContainInOrder(new[] { FeatureDefFolder, FeatureStateFolder },
			because: "a state row keyed on the feature reference is unresolvable without the definition, while a definition alone resolves on its own");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to deliver when the feature is not defined on the environment, and says so.")]
	public void BindFeatureOffState_Should_Refuse_When_The_Feature_Is_Not_Defined() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RemovePanelIconFeature();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeFalse(because: "there is no feature to deliver a state for");
		outcome.Warnings.Should().Contain(warning => warning.Contains("not defined"),
			because: "the user has to learn why the feature toggle was left out of the package");
		environment.SaveBodies.Should().BeEmpty(because: "nothing may be saved for an undefined feature");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to deliver when the feature has no All-Users state row — the feature was never turned off here.")]
	public void BindFeatureOffState_Should_Refuse_When_There_Is_No_AllUsers_State_Row() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RemovePanelIconFeatureState();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeFalse(because: "there is no state row to deliver");
		outcome.Warnings.Should().Contain(warning => warning.Contains("was not turned off here"),
			because: "the report must say the off-state does not exist rather than imply a delivery failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to deliver a state row that is still on — delivering it would turn the feature back on for the install target.")]
	public void BindFeatureOffState_Should_Refuse_When_The_State_Is_Still_On() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeFalse(
			because: "the binding force-updates FeatureState on install, so a still-on row must never ship as an off-state");
		outcome.Warnings.Should().Contain(warning => warning.Contains("still on"),
			because: "the report must name the actual state so the user can decide whether to turn it off and re-run");
		environment.SaveBodies.Should().BeEmpty(because: "neither the state nor the definition may ship");
	}

	[Test]
	[Category("Unit")]
	[TestCase("\"maybe\"")]
	[TestCase("null")]
	[TestCase("{}")]
	[Description("Reports an unreadable FeatureState value with its own wording and refuses to deliver it, exactly like a still-on state.")]
	public void BindFeatureOffState_Should_Report_An_Unreadable_State_As_Unreadable(string featureStateJson) {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(featureStateJson);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeFalse(
			because: "a value that cannot be read as on/off must be refused exactly like a still-on state");
		outcome.Warnings.Should().Contain(warning => warning.Contains("not readable as an on/off value"),
			because: "telling the user the state is 'still on' would send them to turn off a feature that may already be off");
	}

	[Test]
	[Category("Unit")]
	[Description("Delivers the off-state when AdminUnitFeatureState answers the Integer zero a real environment sends on this access path.")]
	public void BindFeatureOffState_Should_Deliver_When_The_Off_State_Is_The_Integer_Zero() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(BrandingFeatureStateWireShape.OffOverSelectQuery);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeTrue(
			because: "the read projection types FeatureState as Integer, so the number 0 is the real off-state and must be accepted");
		ReadJsonArray(environment.SingleSaveBodyFor(FeatureStateFolder), "boundRecordIds")
			.Should().BeEquivalentTo([PanelIconFeatureStateRowId.ToString()],
				because: "the state folder must deliver exactly the confirmed off-state row");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to deliver when AdminUnitFeatureState answers the Integer one — the numeric still-on state.")]
	public void BindFeatureOffState_Should_Refuse_When_The_On_State_Is_The_Integer_One() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(BrandingFeatureStateWireShape.OnOverSelectQuery);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeFalse(
			because: "the number 1 is the numeric still-on state and must be refused like the Boolean one");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts the Boolean false off-state shape too, so a platform change of the wire type cannot silently strand the delivery.")]
	public void BindFeatureOffState_Should_Deliver_When_The_Off_State_Is_The_Boolean_False() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith("false");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeTrue(
			because: "the writable projection types the same column as Boolean, so the Boolean off-shape is equally real");
	}

	[Test]
	[Category("Unit")]
	[TestCase("\"0\"")]
	[TestCase("\"false\"")]
	[Description("Accepts the stringified off-state forms a proxied endpoint can produce.")]
	public void BindFeatureOffState_Should_Deliver_When_The_Off_State_Is_Stringified(string featureStateJson) {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(featureStateJson);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeTrue(
			because: "a stringified scalar must be read the same way as its raw equivalent");
	}

	[Test]
	[Category("Unit")]
	[TestCase("\"1\"")]
	[TestCase("\"true\"")]
	[Description("Refuses the stringified still-on forms the same way as their scalar equivalents.")]
	public void BindFeatureOffState_Should_Refuse_When_The_On_State_Is_Stringified(string featureStateJson) {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(featureStateJson);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Bound.Should().BeFalse(
			because: "a stringified on-state is still an on-state and must not ship as an off-state");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops previously shipped feature folders when the state is no longer confirmed off, so the package stops shipping the wrong toggle.")]
	public void BindFeatureOffState_Should_Drop_Previously_Shipped_Folders_When_No_Longer_Confirmed_Off() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(FeatureStateFolder, ExistingBindingUId);
		environment.RegisterExistingBinding(FeatureDefFolder, Guid.Parse("5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b"));
		environment.LeavePanelIconFeatureOn();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		environment.DeletedBindingNames().Should().Contain(FeatureStateFolder)
			.And.Contain(FeatureDefFolder,
				because: "a state and its definition stand or fall together, and neither may keep shipping a toggle the environment no longer backs");
		outcome.Warnings.Should()
			.Contain(warning => warning.StartsWith(FeatureStateFolder) && warning.Contains("previously shipped binding removed"))
			.And.Contain(warning => warning.StartsWith(FeatureDefFolder) && warning.Contains("previously shipped binding removed"),
				because: "the package now delivers less than it did, which the run summary must surface for both folders");
	}

	[Test]
	[Category("Unit")]
	[Description("Blames the shipping policy, not a missing row, when the feature folders leave the package because the state row reads as still on.")]
	public void BindFeatureOffState_Should_Name_ThePolicy_AsTheRemovalReason_WhenTheStateIsStillOn() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(FeatureStateFolder, ExistingBindingUId);
		environment.LeavePanelIconFeatureOn();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Warnings.Should().Contain(
			warning => warning.StartsWith(FeatureStateFolder) && warning.Contains("must not travel in a package"),
			because: "the state row exists — it is the on-value that bars it from a package, and the removal line must not claim the row is gone");
		outcome.Warnings.Should().NotContain(warning => warning.Contains("no longer has a source row"),
			because: "on this path the row is right there, so that cause would send anyone debugging the run after a deletion that never happened");
	}

	[Test]
	[Category("Unit")]
	[Description("Still blames the missing row when the feature itself is not defined, so the two removal causes stay distinguishable.")]
	public void BindFeatureOffState_Should_Name_TheMissingRow_AsTheRemovalReason_WhenTheFeatureIsUndefined() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(FeatureStateFolder, ExistingBindingUId);
		environment.RemovePanelIconFeature();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindFeatureOffState(FeatureCode);

		// Assert
		outcome.Warnings.Should().Contain(
			warning => warning.StartsWith(FeatureStateFolder) && warning.Contains("no longer has a source row"),
			because: "with no feature row there is genuinely nothing to source, which is the one case that cause describes");
		outcome.Warnings.Should().NotContain(warning => warning.Contains("must not travel in a package"),
			because: "a policy refusal and an absent row are different problems and must not read the same");
	}

	#endregion

	#region Tests: row delivery by Id

	[Test]
	[Category("Unit")]
	[Description("Delivers a row by its own Id, keyed on Id — correct for rows whose id must be preserved across environments.")]
	public void BindRow_Should_Key_The_Row_On_Id() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindRow(
			"SysImage", "ShellBackground", SysImageColumns, BackgroundImageId);

		// Assert
		outcome.Bound.Should().BeTrue(because: "the row exists on this environment");
		string saveBody = environment.SingleSaveBodyFor(ImageFolder);
		ReadJsonString(saveBody, "entitySchemaName").Should().Be("SysImage",
			because: "the folder delivers the entity the caller named");
		ReadJsonArray(saveBody, "boundRecordIds").Should().BeEquivalentTo([BackgroundImageId.ToString()],
			because: "the folder must deliver exactly the row the caller chose");
		KeyColumns(saveBody).Should().BeEquivalentTo(["Id"],
			because: "the row's id is stable across environments, so Id is the correct install-time key");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses and drops the folder when the row is absent, naming the delivery in the caller's terms.")]
	public void BindRow_Should_Refuse_And_Drop_When_The_Row_Is_Absent() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ImageFolder, ExistingBindingUId);
		Guid missingImageId = Guid.Parse("0d9e8f7a-6b5c-4d3e-8f9a-0b1c2d3e4f5a");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		PackageDataBindingOutcome outcome = sut.BindRow(
			"SysImage", "ShellBackground", SysImageColumns, missingImageId);

		// Assert
		outcome.Bound.Should().BeFalse(because: "there is no row to deliver");
		outcome.Warnings.Should().Contain(
			warning => warning.Contains(ImageFolder) && warning.Contains("not found"),
			because: "the report names the folder whose row is gone");
		environment.DeletedBindingNames().Should().Contain(ImageFolder,
			because: "a folder pointing at a deleted row would install an empty snapshot");
	}

	#endregion

	#region Tests: removal

	[Test]
	[Category("Unit")]
	[Description("Removes a folder this delivery owns and reports the removal.")]
	public void Remove_Should_Remove_An_Owned_Folder() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ImageFolder, ExistingBindingUId, "SysImage");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		IReadOnlyList<string> warnings = sut.RemoveBinding(ImageFolder, "SysImage");

		// Assert
		warnings.Should().Contain(warning => warning.Contains("previously shipped binding removed"),
			because: "the folder existed and this delivery owns it, so the run summary must surface the removal");
		environment.DeletedBindingNames().Should().Contain(ImageFolder,
			because: "the registration must actually be deleted from the package");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves a same-name folder that delivers a different schema untouched, with a warning naming the collision.")]
	public void Remove_Should_Leave_A_Foreign_Folder_Untouched() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ImageFolder, ExistingBindingUId, "Contact");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		IReadOnlyList<string> warnings = sut.RemoveBinding(ImageFolder, "SysImage");

		// Assert
		environment.DeleteBodies.Should().BeEmpty(
			because: "deleting a foreign binding would destroy package data this delivery does not own");
		warnings.Should().Contain(warning => warning.Contains("left untouched"),
			because: "the collision must be visible so the user can resolve it");
	}

	[Test]
	[Category("Unit")]
	[Description("Names the caller's withdrawal as the reason a folder left the package, not a missing source row, because on this path the row is still there and the caller chose not to ship it.")]
	public void Remove_Should_Name_TheWithdrawal_AsTheReason() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ImageFolder, ExistingBindingUId);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		IReadOnlyList<string> warnings = sut.RemoveBinding(ImageFolder, "SysImage");

		// Assert
		environment.DeletedBindingNames().Should().Contain(ImageFolder,
			because: "the caller withdrew the folder, so it must stop shipping");
		warnings.Should().Contain(warning => warning.Contains("this delivery no longer ships it"),
			because: "the row is still on the environment — blaming a missing source row would send anyone " +
				"debugging the run looking for a deletion that never happened");
		warnings.Should().NotContain(warning => warning.Contains("no longer has a source row"),
			because: "that cause belongs to the refusal paths, where the row really cannot be sourced");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves a same-name folder untouched when the environment does not report which schema it delivers, because a registration this delivery cannot identify as its own must not be deleted.")]
	public void Remove_Should_Leave_A_Folder_Of_Unreported_Schema_Untouched() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBindingWithoutSchema(ImageFolder, ExistingBindingUId);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		IReadOnlyList<string> warnings = sut.RemoveBinding(ImageFolder, "SysImage");

		// Assert
		environment.DeleteBodies.Should().BeEmpty(
			because: "an unidentifiable registration is as unsafe to delete as a known foreign one — the delete " +
				"endpoint keys on (package, name) alone, so a wrong guess destroys package data outright");
		warnings.Should().Contain(warning => warning.Contains("did not report which entity schema"),
			because: "the reason must be distinct from a reported foreign schema — the two need different fixes");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a same-name folder whose schema the environment does not report, because refreshing it replaces the rows and the schema the registration carries — the same loss the delete path already refuses.")]
	public void BindRow_Should_Refuse_A_Folder_Of_Unreported_Schema() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBindingWithoutSchema(ImageFolder, ExistingBindingUId);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindRow("SysImage", "ShellBackground", SysImageColumns, BackgroundImageId);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a refresh replaces the rows and the schema of a registration this delivery cannot identify")
			.WithMessage("*did not report which entity schema*");
		environment.SaveBodies.Should().BeEmpty(
			because: "nothing may be written under a name whose ownership is unconfirmed");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a same-name folder registered under the all-zero UId, because treating it as no existing binding would register a second folder under the same name instead of refreshing this one.")]
	public void BindSysSettingsValue_Should_Throw_When_The_Existing_Binding_UId_Is_Empty() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding(LoginLogoFolder, Guid.Empty);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		Action act = () => sut.BindSysSettingsValue(LoginLogoCode);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an all-zero UId parses like any other Guid, so only an explicit check keeps it from " +
					"reaching the save as 'no existing binding'")
			.WithMessage("*unusable UId*");
		environment.SaveBodies.Should().BeEmpty(
			because: "a second registration under a name the package already carries is the loss this guard exists " +
				"to prevent, and the next lookup would then fail on 'multiple registrations'");
	}

	[Test]
	[Category("Unit")]
	[Description("Removes both the value folder and the definition folder of a grouped setting the caller withdraws, so the pair leaves the package together.")]
	public void RemoveSysSettingsValue_Should_Remove_The_Value_And_The_Definition() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ConfigFolder, ExistingBindingUId, "SysSettingsValue");
		environment.RegisterExistingBinding(ConfigDefFolder, ExistingBindingUId, "SysSettings");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		IReadOnlyList<string> warnings = sut.RemoveSysSettingsValue(ConfigCode, includeDefinition: true);

		// Assert
		environment.DeletedBindingNames().Should().Contain([ConfigFolder, ConfigDefFolder],
			because: "a definition kept without its value row is a folder nothing in the package uses");
		warnings.Should().HaveCount(2,
			because: "each removal is a gap between what the caller asked for and what the package now ships");
	}

	[Test]
	[Category("Unit")]
	[Description("Withdraws only the value folder when the definition was never part of the delivery, so a product-shipped definition binding is never touched.")]
	public void RemoveSysSettingsValue_Should_Leave_A_Definition_Outside_The_Delivery() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ConfigFolder, ExistingBindingUId, "SysSettingsValue");
		environment.RegisterExistingBinding(ConfigDefFolder, ExistingBindingUId, "SysSettings");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		sut.RemoveSysSettingsValue(ConfigCode);

		// Assert
		environment.DeletedBindingNames().Should().Contain(ConfigFolder,
			because: "the value folder is the one this delivery owns");
		environment.DeletedBindingNames().Should().NotContain(ConfigDefFolder,
			because: "a folder this delivery never claimed is not this delivery's to remove");
	}

	[Test]
	[Category("Unit")]
	[Description("Never deletes a same-name folder that delivers a different schema when withdrawing a setting, and names the collision.")]
	public void RemoveSysSettingsValue_Should_Leave_A_Foreign_Folder_Untouched() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(ConfigFolder, ExistingBindingUId, "Contact");
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		IReadOnlyList<string> warnings = sut.RemoveSysSettingsValue(ConfigCode);

		// Assert
		environment.DeleteBodies.Should().BeEmpty(
			because: "deleting a foreign binding would destroy package data this delivery does not own");
		warnings.Should().Contain(warning => warning.Contains("left untouched"),
			because: "the collision must be visible so the user can resolve it");
	}

	#endregion

	#region Tests: runtime rows are never touched

	[Test]
	[Category("Unit")]
	[Description("Never touches the environment's runtime rows — delivering package data changes only package registrations, even on refusal-and-drop paths.")]
	public void Deliveries_Should_Never_Touch_Runtime_Rows() {
		// Arrange
		DeliveryEnvironment environment = DeliveryEnvironment.WithImageBackground();
		environment.RegisterExistingBinding(LoginLogoFolder, ExistingBindingUId);
		environment.RemoveAllUsersValue(LoginLogoCode);
		IPackageDataBinder sut = CreateBinderFor(environment);

		// Act
		foreach (string code in AllLogoCodes) {
			sut.BindSysSettingsValue(code);
		}
		sut.BindSysSettingsValue(ConfigCode, includeDefinition: true);
		sut.BindRow("SysImage", "ShellBackground", SysImageColumns, BackgroundImageId);
		sut.BindFeatureOffState(FeatureCode);

		// Assert
		environment.RequestedUrls.Should().NotContain(DeleteRowUrl,
			because: "dropping a folder must not delete the environment's own setting value or image");
		environment.RequestedUrls.Should().NotContain(InsertRowUrl,
			because: "delivering registers existing rows, it never creates data");
		environment.RequestedUrls.Should().NotContain(UpdateRowUrl,
			because: "delivering must not rewrite the rows it packages");
	}

	#endregion

	#region Test environment

	/// <summary>
	/// A configurable fake Creatio environment: routes the DataService SelectQuery / SaveSchema /
	/// DeletePackageSchemaData traffic of <see cref="EnvironmentPackageDataBinder"/> against in-memory state
	/// and captures every write so a test can assert the exact delivered payload.
	/// </summary>
	private sealed class DeliveryEnvironment {

		private static readonly IReadOnlyList<string> LogoSettingCodes = [
			"LogoImage", "MenuLogoImage", "ConfigurationPageLogoImage", "CrtAppToolbarLogo",
			"HideSplashScreenLogoImage"
		];

		private const string PanelIconFeatureCode = "UsePanelIconBackground";

		private readonly Dictionary<string, Guid> _settingDefinitions = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _settingValueTypes = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Guid> _allUsersValueRows = new(StringComparer.OrdinalIgnoreCase);

		private Guid? _panelIconFeatureId;
		private Guid? _panelIconFeatureStateRowId;

		/// <summary>
		/// The raw JSON token the AdminUnitFeatureState query answers for <c>FeatureState</c>. A token rather than
		/// a <see cref="bool"/> so a test can model every shape the column is delivered in. Defaults to what a real
		/// environment answers on THIS access path — see <see cref="BrandingFeatureStateWireShape"/>, which also
		/// documents the Boolean shape the ATF path sees and must be revisited together with this one.
		/// </summary>
		private string _panelIconFeatureStateJson = BrandingFeatureStateWireShape.OffOverSelectQuery;
		private readonly Dictionary<string, Guid> _existingBindings = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _existingBindingSchemas = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<string>> _schemaColumns = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<Guid> _existingImages = [];
		private readonly Dictionary<Guid, Guid> _galleryRows = [];
		private readonly HashSet<string> _duplicatedSettingCodes = new(StringComparer.OrdinalIgnoreCase);

		public List<string> SaveBodies { get; } = [];
		public List<string> DeleteBodies { get; } = [];
		public List<string> RequestedUrls { get; } = [];

		/// <summary>Envelope returned by the SaveSchema endpoint, so a test can simulate a rejected save.</summary>
		public string SaveSchemaResponse { get; set; } = """{"success":true}""";

		/// <summary>Envelope returned by the DeletePackageSchemaData endpoint, so a test can simulate a rejection.</summary>
		public string DeleteBindingResponse { get; set; } = """{"success":true}""";

		/// <summary>
		/// Value the CurrentPackageId sys-setting answers with. Defaults to the row id that resolves to this
		/// environment's package; blank models an environment where no current package is set.
		/// </summary>
		public string CurrentPackageIdValue { get; set; } = CurrentPackageRowId.ToString();

		public int PackageInstallType { get; set; }

		private DeliveryEnvironment() {
			_schemaColumns["SysSettingsValue"] = [
				"Id", "SysSettings", "SysAdminUnit", "IsDef",
				"TextValue", "IntegerValue", "FloatValue", "BooleanValue", "DateTimeValue", "GuidValue", "BinaryValue"
			];
			_schemaColumns["SysSettings"] = [
				"Id", "Code", "Name", "ValueTypeName", "IsCacheable", "IsPersonal", "IsSSPAvailable", "Description"
			];
			_schemaColumns["SysImage"] = ["Id", "Name", "Data", "MimeType"];
			_schemaColumns["SysImageInTag"] = ["Id", "Entity", "Tag"];
			_schemaColumns["Feature"] = ["Id", "Code", "Name"];
			_schemaColumns["AdminUnitFeatureState"] = ["Id", "Feature", "SysAdminUnit", "FeatureState"];
		}

		/// <summary>An environment where every logo setting is defined and has an All-Users value row.</summary>
		public static DeliveryEnvironment WithBrandedLogos() {
			DeliveryEnvironment environment = new();
			foreach (string code in LogoSettingCodes) {
				environment.DefineSetting(code);
			}
			return environment;
		}

		/// <summary>An environment branded with an image background: config value, definition, image, gallery row, and a confirmed feature off-state.</summary>
		public static DeliveryEnvironment WithImageBackground() {
			DeliveryEnvironment environment = WithBrandedLogos();
			environment.DefineSetting("CrtBackgroundConfig");
			environment._existingImages.Add(BackgroundImageId);
			environment._galleryRows[BackgroundImageId] = Guid.Parse("8a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
			environment._panelIconFeatureId = PanelIconFeatureId;
			environment._panelIconFeatureStateRowId = PanelIconFeatureStateRowId;
			return environment;
		}

		/// <summary>Models an environment where the feature is defined but was never turned off (no All-Users state row).</summary>
		public void RemovePanelIconFeatureState() => _panelIconFeatureStateRowId = null;

		/// <summary>
		/// Models an All-Users state row that is still on — what <c>--keep-icon-background</c> leaves behind, and
		/// what a swallowed turn-off failure leaves behind too. Uses the numeric on-state a real environment answers.
		/// </summary>
		public void LeavePanelIconFeatureOn() =>
			_panelIconFeatureStateJson = BrandingFeatureStateWireShape.OnOverSelectQuery;

		/// <summary>
		/// Overrides the delivered <c>FeatureState</c> token so a test can cover a shape other than the numeric
		/// default: the Boolean form, a stringified scalar, or a value that is no on/off answer at all.
		/// </summary>
		public void AnswerPanelIconFeatureStateWith(string featureStateJson) =>
			_panelIconFeatureStateJson = featureStateJson;

		/// <summary>Models an environment where the UsePanelIconBackground feature is not defined at all.</summary>
		public void RemovePanelIconFeature() {
			_panelIconFeatureId = null;
			_panelIconFeatureStateRowId = null;
		}

		private void DefineSetting(string code) {
			_settingDefinitions[code] = DeterministicGuid($"def:{code}");
			_allUsersValueRows[code] = DeterministicGuid($"val:{code}");
		}

		public void RemoveAllUsersValue(string code) => _allUsersValueRows.Remove(code);

		/// <summary>
		/// Models an environment where the setting is not defined at all — a different cause from an existing
		/// setting with no All-Users value, and the two must be reported differently.
		/// </summary>
		public void RemoveSettingDefinition(string code) {
			_settingDefinitions.Remove(code);
			_allUsersValueRows.Remove(code);
		}

		/// <summary>Registers a pre-existing binding the delivery owns, under the folder name's own schema prefix.</summary>
		public void RegisterExistingBinding(string bindingName, Guid uId) =>
			RegisterExistingBinding(bindingName, uId, bindingName.Split('_')[0]);

		/// <summary>Registers a pre-existing binding that already delivers the named entity schema.</summary>
		public void RegisterExistingBinding(string bindingName, Guid uId, string entitySchemaName) {
			_existingBindings[bindingName] = uId;
			_existingBindingSchemas[bindingName] = entitySchemaName;
		}

		/// <summary>Registers a pre-existing binding whose entity schema the environment does not report.</summary>
		public void RegisterExistingBindingWithoutSchema(string bindingName, Guid uId) =>
			_existingBindings[bindingName] = uId;

		/// <summary>Redefines a setting's declared value type, e.g. to the secret-bearing SecureText type.</summary>
		public void SetSettingValueType(string code, string valueTypeName) => _settingValueTypes[code] = valueTypeName;

		/// <summary>Makes a setting code resolve to two definition rows, as a corrupted environment would.</summary>
		public void DuplicateSettingDefinition(string code) => _duplicatedSettingCodes.Add(code);

		public void DropSchemaColumn(string schemaName, string columnName) =>
			_schemaColumns[schemaName].RemoveAll(name => string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase));

		/// <summary>The All-Users value row id of a setting, as the delivered payload would reference it.</summary>
		public string ValueRowIdOf(string code) => _allUsersValueRows[code].ToString();

		/// <summary>The definition row id of a setting, as a delivered definition folder would reference it.</summary>
		public string DefinitionRowIdOf(string code) => _settingDefinitions[code].ToString();

		public string SingleSaveBodyFor(string bindingName) =>
			SaveBodies.Should().ContainSingle(body => ReadJsonString(body, "name") == bindingName,
					because: $"exactly one SaveSchema call must target the '{bindingName}' binding folder")
				.Subject;

		public IReadOnlyList<string> SavedBindingNames() =>
			SaveBodies.Select(body => ReadJsonString(body, "name")).ToList();

		public IReadOnlyList<string> DeletedBindingNames() =>
			DeleteBodies.Select(body => ReadJsonString(body, "packageSchemaDataName")).ToList();

		public IPackageDataBinder CreateBinder() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			applicationClient
				.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns(callInfo => Route(callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1)));

			ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
			sysSettingsManager.GetSysSettingValueByCode("CurrentPackageId").Returns(_ => CurrentPackageIdValue);

			IServiceUrlBuilder serviceUrlBuilder = CreateServiceUrlBuilder();
			IPackageTargetResolver targetResolver =
				new PackageTargetResolver(applicationClient, serviceUrlBuilder, sysSettingsManager);
			return new EnvironmentPackageDataBinder(
				applicationClient,
				serviceUrlBuilder,
				new PackageDataBindingWriter(
					applicationClient, serviceUrlBuilder, targetResolver, CreateSchemaClient()),
				targetResolver,
				Substitute.For<ILogger>());
		}

		private string Route(string url, string body) {
			RequestedUrls.Add(url);
			if (url == SaveSchemaUrl) {
				SaveBodies.Add(body);
				return SaveSchemaResponse;
			}
			if (url == DeleteBindingUrl) {
				DeleteBodies.Add(body);
				return DeleteBindingResponse;
			}
			if (url == SelectUrl) {
				return RouteSelect(body);
			}
			throw new InvalidOperationException($"Unexpected request URL: {url}");
		}

		private string RouteSelect(string body) {
			(string schemaName, Dictionary<string, string> filters) = ParseSelect(body);
			return schemaName switch {
				"SysSettings" => LookupSettingDefinition(filters),
				"SysSettingsValue" => Rows("Id", LookupAllUsersValueRow(filters)),
				"SysImage" => Rows("Id", LookupImage(filters)),
				"SysImageInTag" => Rows("Id", LookupGalleryRow(filters)),
				"Feature" => Rows("Id", LookupFeatureDefinition(filters)),
				"AdminUnitFeatureState" => FeatureStateRows(LookupFeatureStateRow(filters)),
				"SysPackage" => LookupPackage(filters),
				"SysPackageSchemaData" => LookupBinding(filters),
				_ => throw new InvalidOperationException($"Unexpected SelectQuery schema: {schemaName}")
			};
		}

		/// <summary>
		/// Answers the SysPackage lookup in both shapes it is asked: filtered by Id, where only this
		/// environment's current package resolves and any other id is a dangling setting, and unfiltered.
		/// </summary>
		private string LookupPackage(Dictionary<string, string> filters) {
			if (filters.TryGetValue("Id", out string? id)) {
				return string.Equals(id, CurrentPackageRowId.ToString(), StringComparison.OrdinalIgnoreCase)
					? PackageRows()
					: """{"success":true,"rows":[]}""";
			}
			return PackageRows();
		}

		private string PackageRows() {
			return $$"""
				{"success":true,"rows":[{"Name":"{{PackageName}}","UId":"{{PackageUId}}","InstallType":{{PackageInstallType}}}]}
				""";
		}

		private string? LookupFeatureDefinition(Dictionary<string, string> filters) =>
			_panelIconFeatureId is not null
			&& filters.TryGetValue("Code", out string? code)
			&& string.Equals(code, PanelIconFeatureCode, StringComparison.Ordinal)
				? _panelIconFeatureId.Value.ToString()
				: null;

		private string? LookupFeatureStateRow(Dictionary<string, string> filters) {
			if (_panelIconFeatureId is null || _panelIconFeatureStateRowId is null) {
				return null;
			}
			return filters.TryGetValue("Feature", out string? feature)
				&& string.Equals(feature, _panelIconFeatureId.Value.ToString(), StringComparison.OrdinalIgnoreCase)
				&& filters.TryGetValue("SysAdminUnit", out string? adminUnit)
				&& string.Equals(adminUnit, AllUsersAdminUnitId.ToString(), StringComparison.OrdinalIgnoreCase)
					? _panelIconFeatureStateRowId.Value.ToString()
					: null;
		}

		private string LookupSettingDefinition(Dictionary<string, string> filters) {
			if (!filters.TryGetValue("Code", out string? code) || !_settingDefinitions.TryGetValue(code, out Guid id)) {
				return """{"success":true,"rows":[]}""";
			}
			string valueType = _settingValueTypes.TryGetValue(code, out string? declared) ? declared : "Text";
			string row = $$"""{"Id":"{{id}}","ValueTypeName":"{{valueType}}"}""";
			string rows = _duplicatedSettingCodes.Contains(code) ? $"{row},{row}" : row;
			return $$"""{"success":true,"rows":[{{rows}}]}""";
		}

		private string? LookupAllUsersValueRow(Dictionary<string, string> filters) {
			if (!filters.TryGetValue("SysSettings", out string? definitionId)
				|| !filters.TryGetValue("SysAdminUnit", out string? adminUnitId)
				|| !string.Equals(adminUnitId, AllUsersAdminUnitId.ToString(), StringComparison.OrdinalIgnoreCase)) {
				return null;
			}
			string? code = _settingDefinitions
				.FirstOrDefault(pair => string.Equals(pair.Value.ToString(), definitionId, StringComparison.OrdinalIgnoreCase))
				.Key;
			return code is not null && _allUsersValueRows.TryGetValue(code, out Guid rowId) ? rowId.ToString() : null;
		}

		private string? LookupImage(Dictionary<string, string> filters) =>
			filters.TryGetValue("Id", out string? id) && Guid.TryParse(id, out Guid imageId)
			&& _existingImages.Contains(imageId)
				? imageId.ToString()
				: null;

		/// <summary>
		/// Answers a SysImageInTag lookup for either shape the delivery uses: a row-existence probe by Id, or
		/// the membership lookup by image and tag.
		/// </summary>
		private string? LookupGalleryRow(Dictionary<string, string> filters) {
			if (filters.TryGetValue("Id", out string? rowIdFilter) && Guid.TryParse(rowIdFilter, out Guid probedRowId)) {
				return _galleryRows.ContainsValue(probedRowId) ? probedRowId.ToString() : null;
			}
			if (!filters.TryGetValue("Entity", out string? entity) || !Guid.TryParse(entity, out Guid imageId)
				|| !_galleryRows.TryGetValue(imageId, out Guid rowId)) {
				return null;
			}
			return rowId.ToString();
		}

		private string LookupBinding(Dictionary<string, string> filters) {
			if (!filters.TryGetValue("Name", out string? name) || !_existingBindings.TryGetValue(name, out Guid uId)) {
				return """{"success":true,"rows":[]}""";
			}
			string schemaName = _existingBindingSchemas.TryGetValue(name, out string? declared) ? declared : null;
			string schemaProperty = schemaName is null ? string.Empty : $""","EntitySchemaName":"{schemaName}" """.TrimEnd();
			return $$"""{"success":true,"rows":[{"UId":"{{uId}}"{{schemaProperty}}}]}""";
		}

		/// <summary>
		/// Answers the AdminUnitFeatureState lookup the way the platform does: the row id together with its
		/// <c>FeatureState</c>, so the delivery can decide whether the row is deliverable at all.
		/// </summary>
		private string FeatureStateRows(string? rowId) =>
			rowId is null
				? """{"success":true,"rows":[]}"""
				: $$"""{"success":true,"rows":[{"Id":"{{rowId}}","FeatureState":{{_panelIconFeatureStateJson}}}]}""";

		private static string Rows(string columnName, string? value) =>
			value is null
				? """{"success":true,"rows":[]}"""
				: $$"""{"success":true,"rows":[{"{{columnName}}":"{{value}}"}]}""";

		private static (string SchemaName, Dictionary<string, string> Filters) ParseSelect(string body) {
			using JsonDocument document = JsonDocument.Parse(body);
			JsonElement root = document.RootElement;
			string schemaName = root.GetProperty("rootSchemaName").GetString() ?? string.Empty;
			Dictionary<string, string> filters = new(StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty filter in root.GetProperty("filters").GetProperty("items").EnumerateObject()) {
				string columnPath = filter.Value.GetProperty("leftExpression").GetProperty("columnPath").GetString() ?? string.Empty;
				JsonElement parameter = filter.Value.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value");
				filters[columnPath] = parameter.ValueKind == JsonValueKind.String
					? parameter.GetString() ?? string.Empty
					: parameter.GetRawText();
			}
			return (schemaName, filters);
		}

		private static IServiceUrlBuilder CreateServiceUrlBuilder() {
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData).Returns(SaveSchemaUrl);
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData).Returns(DeleteBindingUrl);
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Delete).Returns(DeleteRowUrl);
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert).Returns(InsertRowUrl);
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update).Returns(UpdateRowUrl);
			return serviceUrlBuilder;
		}

		private IDataBindingSchemaClient CreateSchemaClient() {
			IDataBindingSchemaClient schemaClient = Substitute.For<IDataBindingSchemaClient>();
			foreach ((string schemaName, List<string> columns) in _schemaColumns) {
				string capturedName = schemaName;
				schemaClient.Fetch(capturedName).Returns(_ => BuildSchema(capturedName, _schemaColumns[capturedName]));
			}
			return schemaClient;
		}

		private static DataBindingSchema BuildSchema(string schemaName, IReadOnlyList<string> columnNames) {
			List<DataBindingSchemaColumn> columns = columnNames
				.Select(name => new DataBindingSchemaColumn(
					DeterministicGuid($"{schemaName}.{name}"),
					name,
					string.Equals(name, "BinaryValue", StringComparison.Ordinal) ? 13 : 1,
					null))
				.ToList();
			return new DataBindingSchema(
				DeterministicGuid($"schema:{schemaName}"),
				schemaName,
				DeterministicGuid($"{schemaName}.Id"),
				columns);
		}

		/// <summary>Stable per-name Guid so payload assertions do not depend on generation order.</summary>
		private static Guid DeterministicGuid(string seed) {
			byte[] bytes = new byte[16];
			int hash = 17;
			foreach (char character in seed) {
				hash = (hash * 31) + character;
			}
			for (int index = 0; index < bytes.Length; index++) {
				hash = (hash * 31) + index;
				bytes[index] = (byte)(hash >> (index % 4 * 8));
			}
			return new Guid(bytes);
		}
	}

	private static IPackageDataBinder CreateBinderFor(DeliveryEnvironment environment) {
		IPackageDataBinder target = environment.CreateBinder();
		target.UsePackage(PackageName);
		return target;
	}

	#endregion

	#region Payload readers

	private static string ReadJsonString(string json, string propertyName) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.GetProperty(propertyName).GetString() ?? string.Empty;
	}

	private static string[] ReadJsonArray(string json, string propertyName) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement
			.GetProperty(propertyName)
			.EnumerateArray()
			.Select(item => item.GetString() ?? string.Empty)
			.ToArray();
	}

	private static string[] KeyColumns(string json) => ColumnsWhereFlagIsSet(json, "isKey");

	private static string[] ForceUpdateColumns(string json) => ColumnsWhereFlagIsSet(json, "isForceUpdate");

	private static string[] ColumnsWhereFlagIsSet(string json, string flagName) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement
			.GetProperty("columns")
			.EnumerateArray()
			.Where(column => column.GetProperty(flagName).GetBoolean())
			.Select(column => column.GetProperty("name").GetString() ?? string.Empty)
			.ToArray();
	}

	#endregion
}
