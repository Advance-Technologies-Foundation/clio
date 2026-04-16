using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Core.Entities;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Property("Module", "Command")]
internal sealed class UpdateEntitySchemaCommandBatchExecutionTests : BaseClioModuleTests
{
	private static readonly Guid PackageUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	private static readonly Guid IdColumnUId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
	private static readonly Guid StatusColumnUId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

	private UpdateEntitySchemaCommand _command = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;
	private IRemoteEntitySchemaDesignerClient _designerClient = null!;
	private ILogger _logger = null!;
	private EntityDesignSchemaDto _loadedSchema = null!;
	private EntityDesignSchemaDto? _savedSchema;

	public override void Setup() {
		base.Setup();
		_loadedSchema = null!;
		_savedSchema = null;
		_command = Container.GetRequiredService<UpdateEntitySchemaCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_designerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_logger = Substitute.For<ILogger>();

		_packageListProvider.GetPackages().Returns([
			new PackageInfo(new PackageDescriptor {
				Name = "UsrPkg",
				UId = PackageUId
			}, string.Empty, Enumerable.Empty<string>())
		]);

		_designerClient.SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(callInfo => {
				_savedSchema = callInfo.ArgAt<EntityDesignSchemaDto>(0);
				return new Clio.Command.EntitySchemaDesigner.SaveDesignItemDesignerResponse {
					Success = true,
					SchemaUId = _savedSchema.UId
				};
			});
		_designerClient.SaveSchemaDbStructure(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new BaseResponse { Success = true });
		_designerClient.GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>())
			.Returns(callInfo => new RuntimeEntitySchemaResponse {
				Success = true,
				Schema = new RuntimeEntitySchemaDto {
					UId = callInfo.ArgAt<Guid>(0),
					Name = (_savedSchema ?? _loadedSchema).Name
				}
			});
		_designerClient.GetAvailableReferenceSchemas(Arg.Any<Clio.Command.EntitySchemaDesigner.GetAvailableSchemasRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new Clio.Command.EntitySchemaDesigner.AvailableEntitySchemasResponse {
				Success = true,
				Items = []
			});
		_designerClient.GetSchemaDesignItem(Arg.Any<Clio.Command.EntitySchemaDesigner.GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(_ => new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Success = true,
				Schema = _savedSchema ?? _loadedSchema
			});

		containerBuilder.AddTransient(_ => _packageListProvider);
		containerBuilder.AddTransient(_ => _designerClient);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Saves and materializes schema changes once for the whole batch instead of once per operation.")]
	public void Execute_Should_Save_And_Materialize_Only_Once_Per_Batch() {
		// Arrange
		_loadedSchema = CreateSchema([
			CreateGuidColumn("Id", IdColumnUId),
			CreateTextColumn("Status", StatusColumnUId, "Old status")
		]);
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"modify","column-name":"Status","title":"Status;Needs review"}""",
				"""{"action":"modify","column-name":"Status","default-value-source":"Const","default-value":"A;B"}"""
			]
		};

		// Act
		int result = _command.Execute(options);
		string? errorMessage = _logger.ReceivedCalls()
			.Select(call => call.GetArguments().FirstOrDefault()?.ToString())
			.LastOrDefault();

		// Assert
		result.Should().Be(0,
			because: $"valid batch updates should succeed when all operations target the same remote schema. Latest logger message: {errorMessage}");
		_designerClient.Received(1).SaveSchema(Arg.Any<EntityDesignSchemaDto>(), Arg.Any<RemoteCommandOptions>());
		_designerClient.Received(1).SaveSchemaDbStructure(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
		_designerClient.Received(1).GetRuntimeEntitySchema(Arg.Any<Guid>(), Arg.Any<RemoteCommandOptions>());
		_savedSchema.Should().NotBeNull(
			because: "the batch should persist the mutated schema once after applying all operations");
		EntitySchemaColumnDto savedColumn = _savedSchema!.Columns!.Single(column => column.Name == "Status");
		EntitySchemaDesignerSupport.GetLocalizableValue(savedColumn.Caption).Should().Be("Status;Needs review",
			because: "semicolons inside the first operation title should survive parsing and batch execution");
		savedColumn.DefValue.Should().NotBeNull(
			because: "later operations in the same batch should still be applied before the single save");
		savedColumn.DefValue!.Value.Should().Be("A;B",
			because: "semicolons inside the second operation default value should survive parsing and batch execution");
	}

	[Test]
	[Description("Keeps an effective title through add then modify batches when the add operation only supplies title-localizations, without synthesizing additional cultures.")]
	public void Execute_Should_PreserveEffectiveTitle_WhenBatchAddsLocalizedColumnThenModifiesDefaultValue() {
		// Arrange
		_loadedSchema = CreateSchema([
			CreateGuidColumn("Id", IdColumnUId)
		]);
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"add","column-name":"UsrStatus","type":"Text","title-localizations":{"en-US":"Status"}}""",
				"""{"action":"modify","column-name":"UsrStatus","default-value-source":"Const","default-value":"Draft"}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "the batch should keep the localized column valid for the later modify operation in the same save");
		EntitySchemaColumnDto savedColumn = _savedSchema!.Columns!.Single(column => column.Name == "UsrStatus");
		savedColumn.Caption.Should().Contain(item => item.CultureName == "en-US" && item.Value == "Status",
			because: "the saved batch should keep the canonical en-US title localization");
		savedColumn.Caption.Should().HaveCount(1,
			because: "Clio must not synthesize additional culture localizations beyond what was explicitly provided");
		savedColumn.DefValue.Should().NotBeNull(
			because: "the later default-value modification should still apply after the localized add");
		savedColumn.DefValue!.Value.Should().Be("Draft",
			because: "the second operation in the batch should still persist its default value");
	}

	private static EntityDesignSchemaDto CreateSchema(IEnumerable<EntitySchemaColumnDto> columns) {
		List<EntitySchemaColumnDto> ownColumns = columns.ToList();
		return new EntityDesignSchemaDto {
			UId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
			Name = "UsrVehicle",
			Package = new Clio.Command.EntitySchemaDesigner.WorkspacePackageDto {
				UId = PackageUId,
				Name = "UsrPkg"
			},
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = "Vehicle"
			}],
			Columns = ownColumns,
			InheritedColumns = [],
			Indexes = [],
			PrimaryColumn = ownColumns.First(column => column.Name == "Id"),
			PrimaryDisplayColumn = ownColumns.FirstOrDefault(column => column.Name == "Status")
		};
	}

	private static EntitySchemaColumnDto CreateGuidColumn(string name, Guid uId) {
		return new EntitySchemaColumnDto {
			UId = uId,
			Name = name,
			DataValueType = 0,
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = name
			}]
		};
	}

	private static EntitySchemaColumnDto CreateTextColumn(string name, Guid uId, string title) {
		return new EntitySchemaColumnDto {
			UId = uId,
			Name = name,
			DataValueType = 1,
			Caption = [new Clio.Command.EntitySchemaDesigner.LocalizableStringDto {
				CultureName = "en-US",
				Value = title
			}]
		};
	}

	private sealed class CultureScope : IDisposable {
		private readonly CultureInfo _originalCurrentCulture;
		private readonly CultureInfo _originalCurrentUiCulture;

		public CultureScope(string cultureName) {
			_originalCurrentCulture = CultureInfo.CurrentCulture;
			_originalCurrentUiCulture = CultureInfo.CurrentUICulture;
			CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;
		}

		public void Dispose() {
			CultureInfo.CurrentCulture = _originalCurrentCulture;
			CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
		}
	}
}
