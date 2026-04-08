using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
internal sealed class EntitySchemaDefaultValueSourceResolverTests
{
	private IRemoteEntitySchemaDesignerClient _designerClient = null!;
	private IEntitySchemaDefaultValueSourceResolver _resolver = null!;

	[SetUp]
	public void Setup() {
		_designerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_resolver = new EntitySchemaDefaultValueSourceResolver(_designerClient);
	}

	[Test]
	[Description("Resolves SystemValue aliases like CurrentDateTime to canonical GUID values before persisting defaults.")]
	public void Resolve_SystemValueAlias_Should_Map_To_Guid() {
		// Arrange
		_designerClient.GetSystemValues(
				Arg.Any<Guid>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SystemValueLookupValueDto {
					Value = Guid.Parse("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d"),
					DisplayValue = "Current Time and Date"
				}
			});

		// Act
		EntitySchemaDefaultValueConfig result = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "CurrentDateTime"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());

		// Assert
		result.ValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "SystemValue aliases must be normalized to canonical GUID values before save");
		result.ResolvedValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "readback should include the stable resolved identifier for API callers");
	}

	[Test]
	[Description("Resolves SystemValue defaults from display captions to canonical GUID values.")]
	public void Resolve_SystemValueCaption_Should_Map_To_Guid() {
		// Arrange
		_designerClient.GetSystemValues(
				Arg.Any<Guid>(),
				Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SystemValueLookupValueDto {
					Value = Guid.Parse("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d"),
					DisplayValue = "Current Time and Date"
				}
			});

		// Act
		EntitySchemaDefaultValueConfig result = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "Current Time and Date"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());

		// Assert
		result.ValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "display captions should be accepted as user-friendly system value selectors");
	}

	[Test]
	[Description("Uses entity-schema runtime mapping for Text500 (30) when requesting available system values.")]
	public void Resolve_SystemValue_Should_Use_EntitySchemaRuntimeMap_For_Text500() {
		// Arrange
		Guid expectedDataValueTypeUId = Guid.Parse("5ca35f10-a101-4c67-a96a-383da6afacfc");
		_designerClient.GetSystemValues(expectedDataValueTypeUId, Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SystemValueLookupValueDto {
					Value = Guid.Parse("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d"),
					DisplayValue = "Current Time and Date"
				}
			});

		// Act
		EntitySchemaDefaultValueConfig result = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "CurrentDateTime"
			},
			dataValueType: 30,
			context: "Column 'UsrLongText'",
			options: new RemoteCommandOptions());

		// Assert
		_designerClient.Received(1).GetSystemValues(expectedDataValueTypeUId, Arg.Any<RemoteCommandOptions>());
		result.ValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "Text500 (dataValueType 30) runtime map resolves to the Text500 UId which matches CurrentDateTime system value");
	}

	[Test]
	[Description("Uses entity-schema runtime mapping for Currency3 (50) when requesting available system values.")]
	public void Resolve_SystemValue_Should_Use_EntitySchemaRuntimeMap_For_Currency3() {
		// Arrange
		Guid expectedDataValueTypeUId = Guid.Parse("969093e2-2b4e-463b-883a-3d3b8c61f0cd");
		_designerClient.GetSystemValues(expectedDataValueTypeUId, Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SystemValueLookupValueDto {
					Value = Guid.Parse("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d"),
					DisplayValue = "Current Time and Date"
				}
			});

		// Act
		EntitySchemaDefaultValueConfig result = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "CurrentDateTime"
			},
			dataValueType: 50,
			context: "Column 'UsrAmount'",
			options: new RemoteCommandOptions());

		// Assert
		_designerClient.Received(1).GetSystemValues(expectedDataValueTypeUId, Arg.Any<RemoteCommandOptions>());
		result.ValueSource.Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "Currency3 (dataValueType 50) runtime map resolves to the Currency UId which matches CurrentDateTime system value");
	}

	[Test]
	[Description("Resolves Settings defaults from code, name, and id to canonical setting code values.")]
	public void Resolve_Settings_Should_Accept_Code_Name_And_Id() {
		// Arrange
		SysSettingsSelectQueryRowDto row = new() {
			Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			Code = "UsrDefaultStartDate",
			Name = "Default Start Date",
			ValueTypeName = "DateTime"
		};
		_designerClient.GetSysSettingsByValueTypeName("DateTime", Arg.Any<RemoteCommandOptions>())
			.Returns(new[] { row });

		// Act
		EntitySchemaDefaultValueConfig fromCode = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "Settings",
				ValueSource = "UsrDefaultStartDate"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());
		EntitySchemaDefaultValueConfig fromName = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "Settings",
				ValueSource = "Default Start Date"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());
		EntitySchemaDefaultValueConfig fromId = _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "Settings",
				ValueSource = "11111111-1111-1111-1111-111111111111"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());

		// Assert
		fromCode.ValueSource.Should().Be("UsrDefaultStartDate",
			because: "setting codes are already canonical and should pass through unchanged");
		fromName.ValueSource.Should().Be("UsrDefaultStartDate",
			because: "setting names should resolve to canonical setting codes before save");
		fromId.ValueSource.Should().Be("UsrDefaultStartDate",
			because: "setting ids should resolve to canonical setting codes before save");
		fromId.ResolvedValueSource.Should().Be("UsrDefaultStartDate",
			because: "readback should expose stable setting codes for resolved defaults");
	}

	[Test]
	[Description("Fails with explicit disambiguation guidance when a setting name matches multiple records.")]
	public void Resolve_SettingsName_Should_Throw_When_Ambiguous() {
		// Arrange
		_designerClient.GetSysSettingsByValueTypeName("DateTime", Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SysSettingsSelectQueryRowDto {
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Code = "UsrSettingA",
					Name = "Default Start Date",
					ValueTypeName = "DateTime"
				},
				new SysSettingsSelectQueryRowDto {
					Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
					Code = "UsrSettingB",
					Name = "Default Start Date",
					ValueTypeName = "DateTime"
				}
			});

		// Act
		Action act = () => _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "Settings",
				ValueSource = "Default Start Date"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*matched multiple setting name values*",
				because: "ambiguous setting names must fail fast instead of persisting unstable defaults");
	}

	[Test]
	[Description("Fails with explicit disambiguation guidance when a setting code matches multiple records.")]
	public void Resolve_SettingsCode_Should_Throw_When_Ambiguous() {
		// Arrange
		_designerClient.GetSysSettingsByValueTypeName("Text", Arg.Any<RemoteCommandOptions>())
			.Returns(new[] {
				new SysSettingsSelectQueryRowDto {
					Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					Code = "SiteUrl",
					Name = "Website URL",
					ValueTypeName = "Text"
				},
				new SysSettingsSelectQueryRowDto {
					Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
					Code = "SiteUrl",
					Name = "Website URL (duplicate)",
					ValueTypeName = "Text"
				}
			});

		// Act
		Action act = () => _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "Settings",
				ValueSource = "SiteUrl"
			},
			dataValueType: 1,
			context: "Column 'UsrWebsiteUrl'",
			options: new RemoteCommandOptions());

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*matched multiple setting code values*",
				because: "ambiguous setting codes should not fall through to the setting-name branch");
	}

	[Test]
	[Description("Fails with actionable guidance when no system variable can be resolved from the provided selector.")]
	public void Resolve_SystemValue_Should_Throw_When_NotFound() {
		// Arrange
		_designerClient.GetSystemValues(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>())
			.Returns(Array.Empty<SystemValueLookupValueDto>());

		// Act
		Action act = () => _resolver.Resolve(
			new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "UnknownAlias"
			},
			dataValueType: 7,
			context: "Column 'UsrStartDate'",
			options: new RemoteCommandOptions());

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>()
			.WithMessage("*has no system variables available*",
				because: "missing system value options should produce a direct error for the caller");
	}
}
