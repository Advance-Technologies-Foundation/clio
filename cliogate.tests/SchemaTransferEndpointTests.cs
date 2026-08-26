using System;
using cliogate.Files.cs;
using cliogate.Files.cs.Dto;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Core;
using Terrasoft.Core.Factories;
using Terrasoft.TestFramework;

namespace cliogate.tests
{
	/// <summary>
	/// Covers the security gate and the request validation of the three schema-transfer endpoints.
	/// </summary>
	/// <remarks>
	/// <c>FindSchemaLayers</c>, <c>ExportSchema</c> and <c>ImportSchema</c> are a privileged surface: they read
	/// <c>SysSchema</c> with a raw <c>Select</c> and write through the platform schema importer. Two properties
	/// therefore have to hold before anything else, and neither of them needs a database: the
	/// <c>CanManageSolution</c> gate refuses an unauthorised caller BEFORE any work is done, and a request that
	/// cannot identify what to act on is refused with a named, actionable reason rather than reaching the
	/// exporter or importer at all.
	/// </remarks>
	[System.ComponentModel.Category("UnitTest")]
	[MockSettings(RequireMock.All)]
	public class SchemaTransferEndpointTests : BaseMarketplaceTestFixture
	{

		#region Constants: Private

		private const string SchemaName = "UsrProbeSchema";
		private const string PackageName = "UsrProbePackage";
		private const string Payload = "{\"Name\":\"UsrProbeSchema\"}";
		private const string CanManageSolution = "CanManageSolution";
		private const string PermissionDeniedMessage =
			"You don't have permission for operation CanManageSolution";

		#endregion

		#region Methods: Private

		private CreatioApiGateway CreateGateway(bool canManageSolution){
			UserConnection.DBSecurityEngine.GetCanExecuteOperation(CanManageSolution).Returns(canManageSolution);
			return new CreatioApiGateway();
		}

		#endregion

		#region Methods: Protected

		protected override void SetUp(){
			base.SetUp();
			ClassFactory.RebindWithFactoryMethod(() => (UserConnection)UserConnection);
		}

		#endregion

		#region Methods: Gate

		[Test]
		[Description("FindSchemaLayers refuses a caller without CanManageSolution, before it reads SysSchema")]
		public void FindSchemaLayers_Throws_When_CanManageSolution_Denied(){
			//Arrange
			CreatioApiGateway sut = CreateGateway(false);

			//Act
			Action act = () => sut.FindSchemaLayers(SchemaName);

			//Assert
			act.Should()
				.Throw<Exception>()
				.WithMessage(PermissionDeniedMessage);
		}

		[Test]
		[Description("ExportSchema refuses a caller without CanManageSolution, before it exports anything")]
		public void ExportSchema_Throws_When_CanManageSolution_Denied(){
			//Arrange
			CreatioApiGateway sut = CreateGateway(false);

			//Act
			Action act = () => sut.ExportSchema(SchemaName, PackageName);

			//Assert
			act.Should()
				.Throw<Exception>()
				.WithMessage(PermissionDeniedMessage);
		}

		[Test]
		[Description("ImportSchema refuses a caller without CanManageSolution, before it writes anything")]
		public void ImportSchema_Throws_When_CanManageSolution_Denied(){
			//Arrange
			// This is the one write endpoint of the three, so the gate failing open here would let an
			// unauthorised caller change configuration schemas.
			CreatioApiGateway sut = CreateGateway(false);

			//Act
			Action act = () => sut.ImportSchema(Payload, PackageName);

			//Assert
			act.Should()
				.Throw<Exception>()
				.WithMessage(PermissionDeniedMessage);
		}

		#endregion

		#region Methods: Request validation

		[Test]
		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		[Description("FindSchemaLayers names the missing argument instead of looking up an empty name")]
		public void FindSchemaLayers_Fails_When_SchemaName_Is_Missing(string schemaName){
			//Arrange
			CreatioApiGateway sut = CreateGateway(true);

			//Act
			FindSchemaLayersResponse response = sut.FindSchemaLayers(schemaName);

			//Assert
			response.Success.Should().BeFalse();
			response.ErrorInfo.Message.Should().Contain("schemaName is required");
			response.Layers.Should().BeEmpty();
		}

		[Test]
		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		[Description("ExportSchema names the missing argument instead of resolving an empty name to a layer")]
		public void ExportSchema_Fails_When_SchemaName_Is_Missing(string schemaName){
			//Arrange
			CreatioApiGateway sut = CreateGateway(true);

			//Act
			ExportSchemaResponse response = sut.ExportSchema(schemaName, PackageName);

			//Assert
			response.Success.Should().BeFalse();
			response.ErrorInfo.Message.Should().Contain("schemaName is required");
			response.SchemaData.Should().BeNull();
			response.Candidates.Should().BeEmpty();
		}

		[Test]
		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		[Description("ImportSchema refuses an empty payload rather than handing it to the platform importer")]
		public void ImportSchema_Fails_When_SchemaData_Is_Missing(string schemaData){
			//Arrange
			CreatioApiGateway sut = CreateGateway(true);

			//Act
			ImportSchemaResponse response = sut.ImportSchema(schemaData, PackageName);

			//Assert
			response.Success.Should().BeFalse();
			response.ErrorInfo.Message.Should().Contain("schemaData is required");
			response.PackageUId.Should().BeNull();
		}

		[Test]
		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		[Description("ImportSchema refuses a request that does not say which package to write into")]
		public void ImportSchema_Fails_When_PackageName_Is_Missing(string packageName){
			//Arrange
			// Without a target package there is nothing to resolve a package UId from, and the importer would
			// otherwise be invoked with Guid.Empty.
			CreatioApiGateway sut = CreateGateway(true);

			//Act
			ImportSchemaResponse response = sut.ImportSchema(Payload, packageName);

			//Assert
			response.Success.Should().BeFalse();
			response.ErrorInfo.Message.Should().Contain("packageName is required");
			response.PackageUId.Should().BeNull();
		}

		#endregion

	}
}
