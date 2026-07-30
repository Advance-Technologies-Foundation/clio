using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.Branding;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <see cref="IBrandingBindingService"/>: per-scope discovery of the live branding rows, the
/// SysSettingsValue natural-key / force-update delivery policy (the correctness core — an Id-keyed binding
/// would insert a duplicate default row on any target that already has the setting), reconcile-on-rerun,
/// and the guarantee that the reconciler never touches the environment's runtime rows.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class BrandingBindingServiceTests {

	private const string PackageName = "UsrBrandingPkg";
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid AllUsersAdminUnitId = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid BackgroundImageId = Guid.Parse("7a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid GalleryRowId = Guid.Parse("8a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid ExistingBindingUId = Guid.Parse("9a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
	/// <summary>The SysPackage row id the environment's CurrentPackageId setting points at.</summary>
	private static readonly Guid CurrentPackageRowId = Guid.Parse("2e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a6b");

	private static readonly Guid PanelIconFeatureId = Guid.Parse("6b1c2d3e-4f50-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid PanelIconFeatureStateRowId = Guid.Parse("7c1d2e3f-4a50-4b6c-8d9e-0f1a2b3c4d5e");

	/// <summary>The platform's well-known shell_background SysImageTag id (see SetBackgroundImageCommand).</summary>
	private static readonly Guid ShellBackgroundTagId = Guid.Parse("273C2402-7CAE-456B-A9C4-067D2024F1A7");

	/// <summary>A customized shell_background tag id, as a non-stock install can carry.</summary>
	private static readonly Guid CustomShellBackgroundTagId = Guid.Parse("5f2e1d0c-3b4a-4958-8766-1a2b3c4d5e6f");

	/// <summary>Every logo slot code plus the splash toggle — the full applied set of a four-slot set-logo run.</summary>
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

	#region Tests: default binding package

	[Test]
	[Category("Unit")]
	[Description("Binds into the package the environment's CurrentPackageId system setting points at when the caller names none.")]
	public void BindBackground_Should_Use_The_CurrentPackageId_Package_When_None_Is_Named() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(null);

		// Assert
		report.Package.Should().Be(PackageName,
			because: "design-time writes land in the environment's current package, so branding follows the same convention instead of a hardcoded well-known package name");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the resolved package on the report so the caller can name it to the user even when it named none itself.")]
	public void BindLogos_Should_Report_The_Named_Package() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		report.Package.Should().Be(PackageName,
			because: "the run summary names the package the branding was delivered into, and only the reconcile knows it when the caller passed none");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops with an actionable error when no package is named and CurrentPackageId is unset, instead of silently picking a well-known package.")]
	public void BindBackground_Should_Throw_When_No_Package_Is_Named_And_CurrentPackageId_Is_Unset() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.CurrentPackageIdValue = string.Empty;
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindBackground(null);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "guessing a package would deliver branding somewhere the user never chose, so the run must stop and ask for one")
			.WithMessage("*CurrentPackageId*");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops with an actionable error when CurrentPackageId points at a package that cannot be resolved on the environment.")]
	public void BindBackground_Should_Throw_When_CurrentPackageId_Does_Not_Resolve() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.CurrentPackageIdValue = "3f2a1b0c-4d5e-4f60-8a9b-0c1d2e3f4a5b";
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindBackground(null);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a dangling current-package id is a configuration problem the user has to see, not a reason to fall back to another package")
			.WithMessage("*CurrentPackageId*");
	}

	[Test]
	[Category("Unit")]
	[Description("Names the caller's package in pre-resolution failure text so an error raised before resolution still says where the branding was headed.")]
	public void DescribeTargetPackage_Should_Name_A_Supplied_Package() {
		// Arrange
		const string named = "UsrMyApp";

		// Act
		string described = BrandingTargetPackage.Describe(named);

		// Assert
		described.Should().Contain(named,
			because: "a failure message written before the package is resolved must still tell the user which package they asked for");
	}

	[Test]
	[Category("Unit")]
	[Description("Names the environment's current package in pre-resolution failure text when the caller supplied none.")]
	public void DescribeTargetPackage_Should_Name_The_Current_Package_When_Blank() {
		// Arrange
		string blankName = "   ";

		// Act
		string described = BrandingTargetPackage.Describe(blankName);

		// Assert
		described.Should().Contain("CurrentPackageId",
			because: "when resolution itself is what failed there is no resolved name to print, so the message must point at the setting the package would have come from");
	}

	#endregion

	#region Tests: Logos scope — natural-key delivery policy

	[Test]
	[Category("Unit")]
	[Description("Binds each applied logo slot's All-Users value row into that slot's own ClioBranding_Logo_* folder against the SysSettingsValue entity.")]
	public void BindLogos_Should_Save_Each_Applied_Slot_Into_Its_Own_Binding() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		string saveBody = environment.SingleSaveBodyFor("ClioBranding_Logo_LogoImage");
		ReadJsonString(saveBody, "entitySchemaName").Should().Be("SysSettingsValue",
			because: "logo branding is delivered as the All-Users SysSettingsValue rows of the logo settings");
		ReadJsonArray(saveBody, "boundRecordIds").Should().BeEquivalentTo([environment.ValueRowIdOf("LogoImage")],
			because: "each slot's folder must deliver exactly that slot's All-Users value row");
		report.Scope.Should().Be(BrandingScope.Logos,
			because: "the report names the branding area it describes");
	}

	[Test]
	[Category("Unit")]
	[Description("Delivers every applied set-logo slot, including the dark-surface toolbar logo, when each has an All-Users value row.")]
	public void BindLogos_Should_Deliver_Every_Applied_Logo_Slot() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		report.Bound.Should().BeEquivalentTo(AllLogoCodes,
			because: "each applied set-logo slot (login, menu, configuration, dark toolbar) plus the splash suppression toggle must travel with the package");
	}

	[Test]
	[Category("Unit")]
	[Description("Never binds a logo slot that was neither applied in this run nor shipped by an earlier run, so the package cannot overwrite a target's own logo with this environment's stock value.")]
	public void BindLogos_Should_Not_Bind_A_Slot_That_Was_Never_Applied_Or_Shipped() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, ["LogoImage"]);

		// Assert
		environment.SavedBindingNames().Should().BeEquivalentTo(["ClioBranding_Logo_LogoImage"],
			because: "an unbranded slot's All-Users row carries this environment's stock value, and force-updating a target with it would overwrite the target's own logo");
		report.Bound.Should().BeEquivalentTo(["LogoImage"],
			because: "the report must list only the slots that actually ship");
	}

	[Test]
	[Category("Unit")]
	[Description("Refreshes a slot that an earlier run shipped even when the current run did not re-apply it, so the packaged snapshot follows the live value.")]
	public void BindLogos_Should_Refresh_A_Previously_Shipped_Slot_That_Was_Not_ReApplied() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding("ClioBranding_Logo_MenuLogoImage", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, ["LogoImage"]);

		// Assert
		environment.SavedBindingNames().Should().BeEquivalentTo(
			["ClioBranding_Logo_LogoImage", "ClioBranding_Logo_MenuLogoImage"],
			because: "a slot the package already ships must stay current with the environment even when this run only applied another slot");
		report.Bound.Should().Contain("MenuLogoImage",
			because: "the refreshed slot still ships and must appear in the report");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an applied code that is not a known logo slot, so a caller bug cannot silently create an unmanaged binding.")]
	public void BindLogos_Should_Throw_When_An_Applied_Code_Is_Unknown() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, ["NoSuchLogoSetting"]);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*NoSuchLogoSetting*",
				because: "an unknown code is a programming error in the caller and must be named instead of silently ignored");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the SysSettingsValue binding on the natural (SysSettings, SysAdminUnit) pair so an install merges onto the target's existing All-Users row.")]
	public void BindLogos_Should_Key_SysSettingsValue_Binding_On_The_Natural_Key() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		string saveBody = environment.SingleSaveBodyFor("ClioBranding_Logo_LogoImage");
		KeyColumns(saveBody).Should().BeEquivalentTo(["SysSettings", "SysAdminUnit"],
			because: "the All-Users default row has a per-environment-random Id, so only the natural key makes an install merge the existing row instead of inserting a duplicate default");
	}

	[Test]
	[Category("Unit")]
	[Description("Never keys the SysSettingsValue binding on Id, because a per-environment-random Id has no counterpart on the target.")]
	public void BindLogos_Should_Not_Key_SysSettingsValue_Binding_On_Id() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor("ClioBranding_Logo_LogoImage")).Should().NotContain("Id",
			because: "keying on the source environment's random Id would insert a duplicate All-Users default row on every target that already has the setting");
	}

	[Test]
	[Category("Unit")]
	[Description("Force-updates the SysSettingsValue value columns so an install overwrites the target's current branding value instead of leaving it in place.")]
	public void BindLogos_Should_ForceUpdate_The_SysSettingsValue_Value_Columns() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		ForceUpdateColumns(environment.SingleSaveBodyFor("ClioBranding_Logo_LogoImage")).Should()
			.BeEquivalentTo(["IsDef", "TextValue", "IntegerValue", "FloatValue", "BooleanValue", "DateTimeValue", "GuidValue", "BinaryValue"],
				because: "a natural-key match that does not force-update the value columns would merge the row without ever applying the branding value");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an applied logo setting that has no All-Users value row as skipped rather than binding a stock value that would overwrite the target's own branding.")]
	public void BindLogos_Should_Report_Setting_Without_AllUsers_Value_As_A_Warning() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RemoveAllUsersValue("MenuLogoImage");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		report.Warnings.Should().ContainSingle(entry => entry.StartsWith("MenuLogoImage:", StringComparison.Ordinal),
			because: "a setting with no All-Users row has nothing deliverable, and the report must say so rather than staying silent about the slot");
	}

	[Test]
	[Category("Unit")]
	[Description("Deletes a slot's binding instead of saving an empty one when the slot no longer has an All-Users value row.")]
	public void BindLogos_Should_Delete_Binding_When_The_Slot_Row_Is_Gone() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RemoveAllAllUsersValues();
		environment.RegisterExistingBinding("ClioBranding_Logo_LogoImage", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		environment.SaveBodies.Should().BeEmpty(
			because: "an empty binding must never be saved; the reconciler deletes the folder instead");
		report.BindingsDropped.Should().BeTrue(
			because: "the stale binding for a slot that is no longer branded must be reconciled away and reported");
	}

	[Test]
	[Category("Unit")]
	[Description("Reuses the existing SysPackageSchemaData UId on a re-run so a refreshed binding updates in place instead of creating a second folder.")]
	public void BindLogos_Should_Reuse_Existing_Binding_UId_When_Refreshing() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding("ClioBranding_Logo_LogoImage", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		ReadJsonString(environment.SingleSaveBodyFor("ClioBranding_Logo_LogoImage"), "uId").Should().Be(ExistingBindingUId.ToString(),
			because: "re-running after a branding change must refresh the delivered snapshot in place, not accumulate duplicate binding folders");
	}

	[Test]
	[Category("Unit")]
	[Description("Creates the binding under a freshly generated UId when the package has never shipped the slot, so a first run creates rather than updating a phantom element.")]
	public void BindLogos_Should_Create_Binding_Under_A_Fresh_UId_When_None_Exists() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, ["LogoImage"]);

		// Assert
		string savedUId = ReadJsonString(environment.SingleSaveBodyFor("ClioBranding_Logo_LogoImage"), "uId");
		savedUId.Should().NotBe(ExistingBindingUId.ToString(),
			because: "with no SysPackageSchemaData element registered for the binding name, the save must create one under a fresh UId instead of addressing any pre-existing element");
	}

	#endregion

	#region Tests: schema projection is never allowed to silently narrow

	[Test]
	[Category("Unit")]
	[Description("Fails loudly when the environment's SysSettingsValue schema is missing a natural-key column, instead of shipping a binding whose key silently narrowed to a wildcard match.")]
	public void BindLogos_Should_Throw_When_A_Key_Column_Is_Missing_From_The_Schema() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.DropSchemaColumn("SysSettingsValue", "SysAdminUnit");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*SysAdminUnit*",
				because: "a key that narrowed to SysSettings alone would match and force-update every value row of the setting on the target, personal overrides included, so the run must stop and name the missing column");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails loudly when a delivered value column is missing from the schema, instead of shipping a binding with an incomplete snapshot.")]
	public void BindLogos_Should_Throw_When_A_Value_Column_Is_Missing_From_The_Schema() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.DropSchemaColumn("SysSettingsValue", "BinaryValue");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*BinaryValue*",
				because: "silently dropping the binary column would ship a logo binding that installs with no image, and the failure would only surface on the target");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails loudly when an Id-keyed schema that carries no column policy is missing a delivered column, where the column-policy validation never runs and the projection guard is the only check.")]
	public void BindBackground_Should_Throw_When_A_Column_Is_Missing_From_A_Schema_Without_A_Column_Policy() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.DropSchemaColumn("SysImage", "Data");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindBackground(PackageName);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Data*",
				because: "SysImage ships with no column policy, so nothing downstream validates its projection — dropping the blob column here would silently ship a background image binding with no image bytes");
	}

	[Test]
	[Category("Unit")]
	[Description("Saves no binding at all when the schema projection is incomplete, so a failed run cannot leave a half-correct binding behind.")]
	public void BindLogos_Should_Not_Save_Any_Binding_When_The_Schema_Projection_Is_Incomplete() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.DropSchemaColumn("SysSettingsValue", "SysAdminUnit");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		try {
			sut.BindLogos(PackageName, AllLogoCodes);
		} catch (InvalidOperationException) {
		}

		// Assert
		environment.SaveBodies.Should().BeEmpty(
			because: "the projection is validated before the SaveSchema call, so an incomplete projection must never reach the package");
	}

	#endregion

	#region Tests: background scope

	[Test]
	[Category("Unit")]
	[Description("Binds the background config value row, its SysSettings definition, the SysImage row, the gallery membership row, and the panel-icon feature off-state and definition when an image background is configured.")]
	public void BindBackground_Should_Bind_All_Background_Folders_For_An_Image_Background() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().BeEquivalentTo([
				"ClioBranding_BackgroundConfig", "ClioBranding_BackgroundConfigDef",
				"ClioBranding_BackgroundImage", "ClioBranding_BackgroundGallery",
				"ClioBranding_PanelIconFeature", "ClioBranding_PanelIconFeatureDef"
			],
			because: "an image background travels only if the config value, its setting definition, the image blob, the gallery membership, and the panel-icon feature off-state all ship together");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not bind the background configuration when the setting is defined as SecureText on this environment, matching the refusal a logo slot already applies.")]
	public void BindBackground_Should_Not_Bind_A_SecureText_Background_Config() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.SetSettingValueType(SetBackgroundImageCommand.BackgroundConfigCode, "SecureText");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_BackgroundConfig",
			because: "a value the environment declares secret-bearing is never shipped in a package, and which branding slot asked for it makes no difference");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the SecureText background configuration as a warning so the caller sees the package ships no background rather than assuming it did.")]
	public void BindBackground_Should_Report_A_SecureText_Background_Config_As_A_Warning() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.SetSettingValueType(SetBackgroundImageCommand.BackgroundConfigCode, "SecureText");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("SecureText"),
			because: "a delivery gap the caller cannot see is a gap they will not fix");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the AdminUnitFeatureState binding on the natural (Feature, SysAdminUnit) pair so an install merges onto the target's existing All-Users state row.")]
	public void BindBackground_Should_Key_The_Panel_Icon_Feature_State_On_The_Natural_Key() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor("ClioBranding_PanelIconFeature")).Should()
			.BeEquivalentTo(["Feature", "SysAdminUnit"],
				because: "the All-Users feature-state row has a per-environment-random Id, so only the natural key makes an install merge the existing row instead of inserting a duplicate");
	}

	[Test]
	[Category("Unit")]
	[Description("Force-updates FeatureState so an install actually turns the feature off on the target instead of leaving its current state in place.")]
	public void BindBackground_Should_ForceUpdate_The_Panel_Icon_Feature_State_Column() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		ForceUpdateColumns(environment.SingleSaveBodyFor("ClioBranding_PanelIconFeature")).Should()
			.BeEquivalentTo(["FeatureState"],
				because: "a natural-key match that does not force-update FeatureState would merge the row without ever turning the feature off");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the Feature definition binding on Id so the state row's Feature reference resolves on the target.")]
	public void BindBackground_Should_Key_The_Panel_Icon_Feature_Definition_On_Id() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor("ClioBranding_PanelIconFeatureDef")).Should().BeEquivalentTo(["Id"],
			because: "the feature definition is usually product-shipped with a stable id, so binding it by id is a no-op on the target and preserves the state row's Feature reference where it was created on demand");
		ReadJsonString(environment.SingleSaveBodyFor("ClioBranding_PanelIconFeatureDef"), "entitySchemaName").Should().Be("Feature",
			because: "the persisted Feature table is what an install can insert into — AppFeature is a virtual UI projection with no table and must never be bound");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the panel-icon feature as skipped when it was never turned off on this environment (no All-Users state row) rather than binding a state that does not exist.")]
	public void BindBackground_Should_Report_Panel_Icon_Feature_As_Skipped_When_Not_Turned_Off() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.RemovePanelIconFeatureState();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("UsePanelIconBackground", StringComparison.Ordinal),
			because: "a feature with no All-Users state row was never turned off here, and the report must say so rather than silently shipping nothing for it");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not bind the panel-icon feature folders when the feature is not defined on this environment.")]
	public void BindBackground_Should_Not_Bind_Panel_Icon_Feature_When_Not_Defined() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.RemovePanelIconFeature();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_PanelIconFeature",
			because: "a feature that does not exist on this environment has no state to ship");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not bind the panel-icon feature state when the All-Users row is still on, so an install cannot force the feature back on and hide the delivered background.")]
	public void BindBackground_Should_Not_Bind_Panel_Icon_Feature_State_When_The_Row_Is_Still_On() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_PanelIconFeature",
			because: "the state binding force-updates FeatureState on install, so shipping a row that is still on — what --keep-icon-background and a swallowed turn-off failure both leave behind — would re-enable the panel icon background on the target and hide the background just delivered");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not bind the panel-icon Feature definition either when the state row is still on, so the package ships no half of an undeliverable feature toggle.")]
	public void BindBackground_Should_Not_Bind_Panel_Icon_Feature_Definition_When_The_Row_Is_Still_On() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_PanelIconFeatureDef",
			because: "the definition folder exists only to make the state row's Feature reference resolve, so it must not ship when no state row is delivered");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the still-on panel-icon feature state as skipped, so a delivery gap the user caused with --keep-icon-background or a failed turn-off is never silent.")]
	public void BindBackground_Should_Report_Panel_Icon_Feature_As_Skipped_When_The_Row_Is_Still_On() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("still on", StringComparison.Ordinal),
			because: "the skipped list is the only channel that reports a delivery gap on both the CLI and the MCP surface, so it must name why the off-state was not shipped");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not bind the panel-icon feature state when FeatureState is no on/off answer at all, because an unverifiable state is not a confirmed off-state.")]
	public void BindBackground_Should_Not_Bind_Panel_Icon_Feature_State_When_It_Is_Unreadable() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith("\"maybe\"");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_PanelIconFeature",
			because: "an unreadable FeatureState must be treated exactly like a still-on one — the binding force-updates the column, so only a value confirmed off may be delivered");
	}

	[Test]
	[Category("Unit")]
	[TestCase("\"maybe\"", TestName = "An unparsable string")]
	[TestCase("null", TestName = "A JSON null")]
	[TestCase("{}", TestName = "A JSON object")]
	[Description("Says the state is not readable rather than that it is still on when FeatureState is no on/off answer at all, because the two need different fixes.")]
	public void BindBackground_Should_Report_An_Unreadable_Feature_State_As_Unreadable(string featureStateJson) {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(featureStateJson);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("not readable as an on/off value"),
			because: "'still on' tells the caller to turn the feature off, which would not help when the platform answered something that is no on/off value at all — the differentiated wording is the whole point of the null branch");
	}

	[Test]
	[Category("Unit")]
	[Description("Says the state is still on — not that it is unreadable — when FeatureState is a readable on-value, so the two null-vs-false branches cannot collapse into one message.")]
	public void BindBackground_Should_Report_A_Readable_On_State_As_Still_On() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("still on"),
			because: "a readable on-state is actionable — the caller can turn the feature off and re-run — so it must not be reported as an unreadable value");
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the panel-icon feature state when the off-state arrives as the integer 0, which is the shape the AdminUnitFeatureState read projection actually delivers.")]
	public void BindBackground_Should_Bind_Panel_Icon_Feature_State_When_The_Off_State_Is_The_Integer_Zero() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith("0");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().Contain("ClioBranding_PanelIconFeature",
			because: "AdminUnitFeatureState declares FeatureState as Integer (dataValueType 4), so a real environment answers a turned-off feature with the number 0 — reading only the Boolean form left the off-state unbound on every environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not bind the panel-icon feature state when the on-state arrives as the integer 1, so the numeric shape is read as an on/off value rather than merely accepted.")]
	public void BindBackground_Should_Not_Bind_Panel_Icon_Feature_State_When_The_On_State_Is_The_Integer_One() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith("1");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_PanelIconFeature",
			because: "accepting numbers must not degrade into accepting any number: 1 is the numeric on-state, and shipping it would force the panel icon background back on for the target");
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the panel-icon feature state when the off-state arrives as the Boolean false, so an environment whose projection types the column as Boolean is still supported.")]
	public void BindBackground_Should_Bind_Panel_Icon_Feature_State_When_The_Off_State_Is_The_Boolean_False() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith("false");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().Contain("ClioBranding_PanelIconFeature",
			because: "the platform types the same FeatureState column as Boolean in its writable AppFeatureState projection, so the Boolean off-state must keep binding alongside the numeric one");
	}

	[Test]
	[Description("Binds the panel-icon feature state when the off-state arrives stringified, so a proxied endpoint that quotes scalars does not silently cost the off-state.")]
	[Category("Unit")]
	[TestCase("\"0\"", TestName = "BindBackground_Should_Bind_Panel_Icon_Feature_State_When_The_Off_State_Is_The_String_Zero")]
	[TestCase("\"false\"", TestName = "BindBackground_Should_Bind_Panel_Icon_Feature_State_When_The_Off_State_Is_The_String_False")]
	public void BindBackground_Should_Bind_Panel_Icon_Feature_State_When_The_Off_State_Is_Stringified(
		string featureStateJson) {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(featureStateJson);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().Contain("ClioBranding_PanelIconFeature",
			because: "a stringified scalar carries the same off-state as its unquoted form, so it must be read the same way instead of falling through to the not-confirmed-off refusal");
	}

	[Test]
	[Description("Does not bind the panel-icon feature state when the on-state arrives stringified, so the string form is interpreted rather than treated as any non-empty value.")]
	[Category("Unit")]
	[TestCase("\"1\"", TestName = "BindBackground_Should_Not_Bind_Panel_Icon_Feature_State_When_The_On_State_Is_The_String_One")]
	[TestCase("\"true\"", TestName = "BindBackground_Should_Not_Bind_Panel_Icon_Feature_State_When_The_On_State_Is_The_String_True")]
	public void BindBackground_Should_Not_Bind_Panel_Icon_Feature_State_When_The_On_State_Is_Stringified(
		string featureStateJson) {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.AnswerPanelIconFeatureStateWith(featureStateJson);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_PanelIconFeature",
			because: "a stringified on-state is still an on-state, so it must be refused exactly like its unquoted form");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops a previously shipped panel-icon feature binding when the All-Users row is on again, so a package that once delivered the off-state stops delivering the wrong one.")]
	public void BindBackground_Should_Drop_Panel_Icon_Feature_Binding_When_The_Row_Is_Still_On() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		environment.RegisterExistingBinding("ClioBranding_PanelIconFeature", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.BindingsDropped.Should().BeTrue(
			because: "leaving the earlier off-state binding in place would keep force-updating the target from a row that is no longer off, so the reconcile must delete it and report the change");
	}

	[Test]
	[Category("Unit")]
	[Description("Still binds the other background folders when the panel-icon feature state is not deliverable, so one undeliverable toggle does not cost the whole background.")]
	public void BindBackground_Should_Still_Bind_The_Background_When_Panel_Icon_Feature_Is_Still_On() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.LeavePanelIconFeatureOn();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().BeEquivalentTo([
				"ClioBranding_BackgroundConfig", "ClioBranding_BackgroundConfigDef",
				"ClioBranding_BackgroundImage", "ClioBranding_BackgroundGallery"
			],
			because: "the feature toggle is one scope member among five; refusing to ship it must not suppress the config, definition, image, and gallery folders the run did apply");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops a previously shipped panel-icon feature binding when the feature is no longer turned off, and reports it, so a dropped binding is never a silent side effect.")]
	public void BindBackground_Should_Drop_Panel_Icon_Feature_Binding_When_No_Longer_Turned_Off() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.RemovePanelIconFeatureState();
		environment.RegisterExistingBinding("ClioBranding_PanelIconFeature", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.BindingsDropped.Should().BeTrue(
			because: "a reconcile that dropped a previously shipped feature binding changed the package and must be reported as an update");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys the SysImage binding on Id, because the image row is clio-generated on the source and has no counterpart on the target.")]
	public void BindBackground_Should_Key_The_Background_Image_Binding_On_Id() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		KeyColumns(environment.SingleSaveBodyFor("ClioBranding_BackgroundImage")).Should().BeEquivalentTo(["Id"],
			because: "a clio-generated image row has no natural counterpart on the target, so a plain insert-by-Id is the correct delivery");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an unreadable background configuration value as an explicit skip so a corrupted config cannot look like a deliberate switch to a colour background.")]
	public void BindBackground_Should_Report_Unreadable_Background_Config_As_A_Warning() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.BackgroundConfigJson = "{ this is not valid json";
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("not readable", StringComparison.Ordinal),
			because: "a parse failure and a colour background both yield no image id, but only the parse failure means the packaged background may be silently wrong");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a colour background distinctly from an unreadable one, so the report explains why no image was bound.")]
	public void BindBackground_Should_Report_Colour_Background_As_Skipped_Image() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.BackgroundConfigJson = """{"colour":"#102030"}""";
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("is a colour", StringComparison.Ordinal),
			because: "a colour background legitimately has no image, and the report must say so rather than staying silent about the image folders");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the image and gallery bindings it reconciles away when the background stops being an image, so a dropped binding is never a silent side effect.")]
	public void BindBackground_Should_Report_Image_Bindings_It_Drops_When_Background_Is_No_Longer_An_Image() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.BackgroundConfigJson = """{"colour":"#102030"}""";
		environment.RegisterExistingBinding("ClioBranding_BackgroundImage", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should()
			.Contain(entry => entry.Contains("ClioBranding_BackgroundImage", StringComparison.Ordinal)
				&& entry.Contains("removed", StringComparison.Ordinal),
				because: "removing a previously shipped binding changes what the package delivers and must appear in the report");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the background scope as having removed something when a reconcile drops a previously shipped binding, matching how the setting scopes report the same event.")]
	public void BindBackground_Should_Mark_Scope_As_Removed_When_It_Drops_A_Binding() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.BackgroundConfigJson = """{"colour":"#102030"}""";
		environment.RegisterExistingBinding("ClioBranding_BackgroundImage", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.BindingsDropped.Should().BeTrue(
			because: "a reconcile that dropped a shipped binding changed the package, so the run summary must say the bindings were updated rather than plainly added");
	}

	#endregion

	#region Tests: a reconcile never touches runtime rows

	[Test]
	[Category("Unit")]
	[Description("Never issues a row-level DeleteQuery, InsertQuery, or UpdateQuery, so the environment's own images and settings always survive a reconcile.")]
	public void BindBackground_Should_Never_Touch_Runtime_Rows() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.BackgroundConfigJson = """{"colour":"#102030"}""";
		environment.RegisterExistingBinding("ClioBranding_BackgroundImage", ExistingBindingUId);
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.RequestedUrls.Should().NotContain(url => url == DeleteRowUrl || url == InsertRowUrl || url == UpdateRowUrl,
			because: "the reconciler manages package bindings only; deleting or mutating the environment's runtime images and settings would destroy the live branding");
	}

	#endregion

	#region Tests: input validation

	[Test]
	[Category("Unit")]
	[Description("Rejects a package that does not exist on the environment and points the caller at list-packages.")]
	public void BindLogos_Should_Throw_When_Package_Is_Not_Found() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos("NoSuchPackage", AllLogoCodes);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*list-packages*",
				because: "an unknown package name is a caller mistake and the error must name the command that lists the valid names");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats a whitespace-only package name as 'none supplied' and resolves the environment's current package, so a padded argument does not become a package name that cannot exist.")]
	public void BindLogos_Should_Resolve_The_Current_Package_When_The_Name_Is_Blank() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos("   ", AllLogoCodes);

		// Assert
		report.Package.Should().Be(PackageName,
			because: "a blank argument carries no choice, so it must take the same CurrentPackageId path as an omitted one rather than being looked up verbatim");
	}

	#endregion

	#region Tests: delivery-safety guards

	[Test]
	[Category("Unit")]
	[Description("Withholds the gallery membership binding when the environment's shell_background tag carries a customized id, because the bound row references the tag by id and that id has no counterpart on an install target.")]
	public void BindBackground_Should_Not_Bind_Gallery_Membership_Registered_Under_A_Customized_Tag() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.GalleryTagId = CustomShellBackgroundTagId;
		environment.NamedShellBackgroundTagId = CustomShellBackgroundTagId;
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_BackgroundGallery",
			because: "shipping a membership row whose Tag id does not exist on the target would either break the install on the foreign key or land a dangling row that never shows the image in the gallery");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the withheld gallery membership so the operator learns the background ships without its gallery entry instead of discovering it after an install.")]
	public void BindBackground_Should_Report_Gallery_Membership_Withheld_For_A_Customized_Tag() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.GalleryTagId = CustomShellBackgroundTagId;
		environment.NamedShellBackgroundTagId = CustomShellBackgroundTagId;
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("customized id"),
			because: "a delivery gap that only surfaces on install must be an explicit report entry, not silent behaviour");
	}

	[Test]
	[Category("Unit")]
	[Description("Still binds the background image itself when only the gallery membership is withheld, because the image row is Id-keyed and transfers safely on its own.")]
	public void BindBackground_Should_Still_Bind_The_Background_Image_When_The_Gallery_Tag_Is_Customized() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.GalleryTagId = CustomShellBackgroundTagId;
		environment.NamedShellBackgroundTagId = CustomShellBackgroundTagId;
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindBackground(PackageName);

		// Assert
		environment.SavedBindingNames().Should().Contain("ClioBranding_BackgroundImage",
			because: "the image row carries no cross-environment reference, so withholding the membership must not withhold the image too");
	}

	[Test]
	[Category("Unit")]
	[Description("Never binds a branding setting that this environment defines as SecureText, because the package would carry the encrypted secret off the environment.")]
	public void BindLogos_Should_Not_Bind_A_Setting_Defined_As_SecureText() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.SetSettingValueType("LogoImage", "SecureText");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		environment.SavedBindingNames().Should().NotContain("ClioBranding_Logo_LogoImage",
			because: "a SecureText value holds an encrypted secret, and a package must never carry a secret off the environment that owns it");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the SecureText setting as skipped so the operator sees why that logo did not ship.")]
	public void BindLogos_Should_Report_A_SecureText_Setting_As_A_Warning() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.SetSettingValueType("LogoImage", "SecureText");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("SecureText"),
			because: "silently omitting one logo would look identical to that logo never having been branded");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to reuse a same-named binding that already delivers a different entity schema instead of silently re-saving it under the branding schema.")]
	public void BindLogos_Should_Throw_When_An_Existing_Binding_Delivers_A_Different_Schema() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RegisterExistingBinding("ClioBranding_Logo_LogoImage", ExistingBindingUId, "Contact");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Contact*",
				because: "re-saving a binding the command does not own would destroy whatever that binding delivered, so the collision is named and the run stops");
	}

	[Test]
	[Category("Unit")]
	[Description("Names the undefined setting as the cause when a logo slot has no definition on the environment, instead of reporting it as a missing All-Users value.")]
	public void BindLogos_Should_Report_An_Undefined_Setting_As_Its_Own_Cause() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RemoveSettingDefinition("CrtAppToolbarLogo");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("CrtAppToolbarLogo") && entry.Contains("not defined"),
			because: "an absent setting and a setting with no All-Users value need different fixes, so reporting both as 'no All-Users value' sends the caller looking in the wrong place");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not delete a same-named binding that delivers a different entity schema when dropping a slot, because DeletePackageSchemaData keys on the package and name alone.")]
	public void BindLogos_Should_Not_Delete_A_Binding_That_Delivers_A_Different_Schema() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RemoveAllUsersValue("LogoImage");
		environment.RegisterExistingBinding("ClioBranding_Logo_LogoImage", ExistingBindingUId, "Contact");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		environment.DeletedBindingNames().Should().NotContain("ClioBranding_Logo_LogoImage",
			because: "the delete request carries only the package and the binding name, so deleting a name branding does not own would destroy another owner's package data outright — worse than the overwrite the save path already refuses");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the untouched foreign-schema binding as a warning so the caller learns the branding name is taken rather than silently getting no delivery.")]
	public void BindLogos_Should_Warn_When_A_Foreign_Schema_Binding_Blocks_A_Drop() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RemoveAllUsersValue("LogoImage");
		environment.RegisterExistingBinding("ClioBranding_Logo_LogoImage", ExistingBindingUId, "Contact");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		report.Warnings.Should().Contain(entry => entry.Contains("Contact"),
			because: "the caller has to know the branding name is occupied by someone else's binding to be able to rename or remove it");
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses to bind when a setting code resolves to more than one definition rather than picking an arbitrary one to deliver.")]
	public void BindLogos_Should_Throw_When_A_Setting_Has_Duplicate_Definitions() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.DuplicateSettingDefinition("LogoImage");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*multiple definitions*",
				because: "binding an arbitrary duplicate would force-update the target from a row the caller never chose");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails the run when the platform rejects the SaveSchema call, so a failed delivery is never reported as a successful bind.")]
	public void BindLogos_Should_Throw_When_SaveSchema_Is_Rejected() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.SaveSchemaResponse = """{"success":false,"errorInfo":{"message":"package is locked"}}""";
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		act.Should().Throw<Exception>()
			.WithMessage("*SaveSchema*",
				because: "a rejected save must surface as a failure; reporting 'branding data added' after it would tell the user the package ships branding it does not have");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails the run when the platform rejects a reconcile-away binding delete, so a stale binding the package still ships is never reported as removed.")]
	public void BindLogos_Should_Throw_When_Binding_Delete_Is_Rejected() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithBrandedLogos();
		environment.RemoveAllAllUsersValues();
		environment.RegisterExistingBinding("ClioBranding_Logo_LogoImage", ExistingBindingUId);
		environment.DeleteBindingResponse = """{"success":false,"errorInfo":{"message":"package is locked"}}""";
		IBrandingBindingService sut = environment.CreateService();

		// Act
		Action act = () => sut.BindLogos(PackageName, AllLogoCodes);

		// Assert
		act.Should().Throw<Exception>()
			.WithMessage("*DeletePackageSchemaData*",
				because: "a rejected delete must surface as a failure so the caller does not believe the package stopped shipping the branding");
	}

	[Test]
	[Category("Unit")]
	[Description("Distinguishes an environment with no background configured at all from one deliberately set to a colour background.")]
	public void BindBackground_Should_Report_An_Unconfigured_Background_Distinctly_From_A_Colour_Background() {
		// Arrange
		BrandingEnvironment environment = BrandingEnvironment.WithImageBackground();
		environment.BackgroundConfigJson = null;
		environment.RemoveAllUsersValue("CrtBackgroundConfig");
		IBrandingBindingService sut = environment.CreateService();

		// Act
		BrandingScopeReport report = sut.BindBackground(PackageName);

		// Assert
		report.Warnings.Should().NotContain(entry => entry.Contains("colour"),
			because: "an environment that never had a background must not be reported as having chosen a colour one");
	}

	#endregion

	#region Test environment

	/// <summary>
	/// A configurable fake Creatio environment: routes the DataService SelectQuery / SaveSchema /
	/// DeletePackageSchemaData traffic of <c>BrandingBindingService</c> against in-memory branding state and
	/// captures every write so a test can assert the exact delivered payload.
	/// </summary>
	private sealed class BrandingEnvironment {

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
		public string? BackgroundConfigJson { get; set; }

		/// <summary>The tag id the gallery membership row is registered under. Defaults to the well-known one.</summary>
		public Guid GalleryTagId { get; set; } = ShellBackgroundTagId;

		/// <summary>Id returned when the service re-resolves the shell_background tag by name; null when absent.</summary>
		public Guid? NamedShellBackgroundTagId { get; set; }

		/// <summary>Envelope returned by the SaveSchema endpoint, so a test can simulate a rejected save.</summary>
		public string SaveSchemaResponse { get; set; } = """{"success":true}""";

		/// <summary>Envelope returned by the DeletePackageSchemaData endpoint, so a test can simulate a rejection.</summary>
		public string DeleteBindingResponse { get; set; } = """{"success":true}""";

		/// <summary>
		/// Value the CurrentPackageId sys-setting answers with. Defaults to the row id that resolves to this
		/// environment's package; blank models an environment where no current package is set.
		/// </summary>
		public string CurrentPackageIdValue { get; set; } = CurrentPackageRowId.ToString();

		private BrandingEnvironment() {
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
		public static BrandingEnvironment WithBrandedLogos() {
			BrandingEnvironment environment = new();
			foreach (string code in LogoSettingCodes) {
				environment.DefineSetting(code);
			}
			return environment;
		}

		/// <summary>An environment branded with an image background: config value, definition, image, and gallery row.</summary>
		public static BrandingEnvironment WithImageBackground() {
			BrandingEnvironment environment = WithBrandedLogos();
			environment.DefineSetting("CrtBackgroundConfig");
			environment.BackgroundConfigJson = $$"""{"imageId":"{{BackgroundImageId}}"}""";
			environment._existingImages.Add(BackgroundImageId);
			environment._galleryRows[BackgroundImageId] = GalleryRowId;
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

		public void RemoveAllAllUsersValues() => _allUsersValueRows.Clear();

		public void RegisterExistingBinding(string bindingName, Guid uId) => _existingBindings[bindingName] = uId;

		/// <summary>Registers a pre-existing binding that already delivers a different entity schema.</summary>
		public void RegisterExistingBinding(string bindingName, Guid uId, string entitySchemaName) {
			_existingBindings[bindingName] = uId;
			_existingBindingSchemas[bindingName] = entitySchemaName;
		}

		/// <summary>Redefines a setting's declared value type, e.g. to the secret-bearing SecureText type.</summary>
		public void SetSettingValueType(string code, string valueTypeName) => _settingValueTypes[code] = valueTypeName;

		/// <summary>Makes a setting code resolve to two definition rows, as a corrupted environment would.</summary>
		public void DuplicateSettingDefinition(string code) => _duplicatedSettingCodes.Add(code);

		public void DropSchemaColumn(string schemaName, string columnName) =>
			_schemaColumns[schemaName].RemoveAll(name => string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase));

		/// <summary>The All-Users value row id of a setting, as the delivered payload would reference it.</summary>
		public string ValueRowIdOf(string code) => _allUsersValueRows[code].ToString();

		public string SingleSaveBodyFor(string bindingName) =>
			SaveBodies.Should().ContainSingle(body => ReadJsonString(body, "name") == bindingName,
					because: $"exactly one SaveSchema call must target the '{bindingName}' binding folder")
				.Subject;

		public IReadOnlyList<string> SavedBindingNames() =>
			SaveBodies.Select(body => ReadJsonString(body, "name")).ToList();

		public IReadOnlyList<string> DeletedBindingNames() =>
			DeleteBodies.Select(body => ReadJsonString(body, "packageSchemaDataName")).ToList();

		public IBrandingBindingService CreateService() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			applicationClient
				.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns(callInfo => Route(callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1)));

			ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
			sysSettingsManager.GetAllUsersDefaultByCode("CrtBackgroundConfig").Returns(_ => BackgroundConfigJson);
			sysSettingsManager.GetSysSettingValueByCode("CurrentPackageId").Returns(_ => CurrentPackageIdValue);

			return new BrandingBindingService(
				applicationClient,
				CreateServiceUrlBuilder(),
				CreatePackageListProvider(),
				CreateSchemaClient(),
				sysSettingsManager,
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
				"SysImageTag" => Rows("Id", NamedShellBackgroundTagId?.ToString()),
				"Feature" => Rows("Id", LookupFeatureDefinition(filters)),
				"AdminUnitFeatureState" => FeatureStateRows(LookupFeatureStateRow(filters)),
				"SysPackage" => LookupPackage(filters),
				"SysPackageSchemaData" => LookupBinding(filters),
				_ => throw new InvalidOperationException($"Unexpected SelectQuery schema: {schemaName}")
			};
		}

		/// <summary>
		/// Answers the SysPackage lookup behind the CurrentPackageId fallback: the row this environment's current
		/// package points at resolves to its name and UId, and any other id resolves to nothing (a dangling
		/// current-package setting).
		/// </summary>
		private string LookupPackage(Dictionary<string, string> filters) =>
			filters.TryGetValue("Id", out string? id)
			&& string.Equals(id, CurrentPackageRowId.ToString(), StringComparison.OrdinalIgnoreCase)
				? $$"""{"success":true,"rows":[{"Name":"{{PackageName}}","UId":"{{PackageUId}}"}]}"""
				: """{"success":true,"rows":[]}""";

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
		/// Honors the Tag filter as the real SysImageInTag query does: a membership registered under a customized
		/// tag id is invisible to a lookup for the well-known one.
		/// </summary>
		private string? LookupGalleryRow(Dictionary<string, string> filters) {
			if (!filters.TryGetValue("Entity", out string? entity) || !Guid.TryParse(entity, out Guid imageId)
				|| !_galleryRows.TryGetValue(imageId, out Guid rowId)) {
				return null;
			}
			return filters.TryGetValue("Tag", out string? tag) && Guid.TryParse(tag, out Guid tagId)
				&& tagId == GalleryTagId
					? rowId.ToString()
					: null;
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
		/// <c>FeatureState</c>, so the binding service can decide whether the row is deliverable at all.
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

		private static IApplicationPackageListProvider CreatePackageListProvider() {
			IApplicationPackageListProvider packageListProvider = Substitute.For<IApplicationPackageListProvider>();
			packageListProvider.GetPackages().Returns([
				new PackageInfo(
					new PackageDescriptor { Name = PackageName, UId = PackageUId },
					string.Empty,
					Enumerable.Empty<string>())
			]);
			return packageListProvider;
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
