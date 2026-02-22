using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.ModelBuilder;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.ModelBuilder;

[TestFixture]
internal class ModelBuilderTests
{
	[Test]
	public void GetModels_UsesServiceUrlBuilderKnownRoutes()
	{
		// Arrange
		string tempPath = Path.Combine(Path.GetTempPath(), $"clio-modelbuilder-tests-{Guid.NewGuid():N}");
		try {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();

			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest)
				.Returns("http://localhost/entity-schema-manager");
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
				.Returns("http://localhost/runtime-entity-schema");

			applicationClient.ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty)
				.Returns("{\"collection\":[{\"name\":\"Contact\"}]}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema", Arg.Any<string>())
				.Returns("{\"schema\":{\"columns\":{\"Items\":{}}}}");

			AddItemOptions options = new() {
				CreateAll = true,
				Culture = "en-US",
				DestinationPath = tempPath,
				Namespace = "Clio.Tests.Generated"
			};

			var modelBuilder = new Clio.ModelBuilder.ModelBuilder(
				applicationClient,
				workingDirectoriesProvider,
				serviceUrlBuilder);

			// Act
			modelBuilder.GetModels(options);

			// Assert
			serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest);
			serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest);
			applicationClient.Received(1).ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty);
			applicationClient.Received(1).ExecutePostRequest("http://localhost/runtime-entity-schema", Arg.Any<string>());
		}
		finally {
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
		}
	}

	[Test]
	public void GetModels_GeneratesReverseDetailConnectionInDetailsRegion()
	{
		// Arrange
		string tempPath = Path.Combine(Path.GetTempPath(), $"clio-modelbuilder-tests-{Guid.NewGuid():N}");
		try {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();

			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest)
				.Returns("http://localhost/entity-schema-manager");
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
				.Returns("http://localhost/runtime-entity-schema");

			applicationClient.ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty)
				.Returns("{\"collection\":[{\"name\":\"Account\"},{\"name\":\"AccountFile\"}]}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"Account\"}")
				.Returns("{\"schema\":{\"columns\":{\"Items\":{}}}}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"AccountFile\"}")
				.Returns(
					"{\"schema\":{\"columns\":{\"Items\":{\"a4a4a4a4-0000-0000-0000-000000000001\":{\"name\":\"Account\",\"dataValueType\":10,\"referenceSchemaName\":\"Account\",\"isRequired\":false}}}}}");

			AddItemOptions options = new() {
				CreateAll = true,
				Culture = "en-US",
				DestinationPath = tempPath,
				Namespace = "CreatioModel"
			};

			var modelBuilder = new Clio.ModelBuilder.ModelBuilder(
				applicationClient,
				workingDirectoriesProvider,
				serviceUrlBuilder);

			// Act
			modelBuilder.GetModels(options);

			// Assert
			string accountModelText = File.ReadAllText(Path.Combine(tempPath, "Account.cs"));
			accountModelText.Should().Contain("#region Details");
			accountModelText.Should().Contain("[DetailProperty(nameof(global::CreatioModel.AccountFile.AccountId))]");
			accountModelText.Should().Contain("public virtual List<AccountFile> CollectionOfAccountFileByAccount { get; set; }");
			accountModelText.Should().Contain("#endregion");

			string accountFileModelText = File.ReadAllText(Path.Combine(tempPath, "AccountFile.cs"));
			accountFileModelText.Should().Contain("[LookupProperty(\"Account\")]");
			accountFileModelText.Should().Contain("public virtual Account Account { get; set; }");
		}
		finally {
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
		}
	}

	[Test]
	public void GetModels_DoesNotGenerateReverseDetailForLookupNamedId()
	{
		// Arrange
		string tempPath = Path.Combine(Path.GetTempPath(), $"clio-modelbuilder-tests-{Guid.NewGuid():N}");
		try {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();

			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest)
				.Returns("http://localhost/entity-schema-manager");
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
				.Returns("http://localhost/runtime-entity-schema");

			applicationClient.ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty)
				.Returns("{\"collection\":[{\"name\":\"Master\"},{\"name\":\"Detail\"}]}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"Master\"}")
				.Returns("{\"schema\":{\"columns\":{\"Items\":{}}}}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"Detail\"}")
				.Returns(
					"{\"schema\":{\"columns\":{\"Items\":{\"a4a4a4a4-0000-0000-0000-000000000001\":{\"name\":\"Id\",\"dataValueType\":10,\"referenceSchemaName\":\"Master\", \"isRequired\":true}}}}}");

			AddItemOptions options = new() {
				CreateAll = true,
				Culture = "en-US",
				DestinationPath = tempPath,
				Namespace = "CreatioModel"
			};

			var modelBuilder = new Clio.ModelBuilder.ModelBuilder(
				applicationClient,
				workingDirectoriesProvider,
				serviceUrlBuilder);

			// Act
			modelBuilder.GetModels(options);

			// Assert
			string masterModelText = File.ReadAllText(Path.Combine(tempPath, "Master.cs"));
			masterModelText.Should().NotContain("IdId");
			masterModelText.Should().NotContain("CollectionOfDetail");
			masterModelText.Should().NotContain("#region Details");
		}
		finally {
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
		}
	}
}
