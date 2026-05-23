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
[Category("Unit")]
[Property("Module", "ModelBuilder")]
internal class ModelBuilderTests
{
	[Test]
	[Description("Uses the known service URL builder routes when generating all models so schema discovery and runtime-schema retrieval stay aligned with the registered service paths.")]
	public void GetModels_UsesServiceUrlBuilderKnownRoutes()
	{
		// Arrange
		string tempPath = Path.Combine(Path.GetTempPath(), $"clio-modelbuilder-tests-{Guid.NewGuid():N}");
		try {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
			ILogger logger = Substitute.For<ILogger>();

			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest)
				.Returns("http://localhost/entity-schema-manager");
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
				.Returns("http://localhost/runtime-entity-schema");

			applicationClient.ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns("{\"collection\":[{\"name\":\"Contact\"}]}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
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
				serviceUrlBuilder,
				logger);

			// Act
			modelBuilder.GetModels(options);

			// Assert
			serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest);
			serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest);
			applicationClient.Received(1).ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
			applicationClient.Received(1).ExecutePostRequest("http://localhost/runtime-entity-schema", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		}
		finally {
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
		}
	}

	[Test]
	[Description("Generates reverse detail collection properties for lookup-based one-to-many relationships in the details region of the master model.")]
	public void GetModels_GeneratesReverseDetailConnectionInDetailsRegion()
	{
		// Arrange
		string tempPath = Path.Combine(Path.GetTempPath(), $"clio-modelbuilder-tests-{Guid.NewGuid():N}");
		try {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
			ILogger logger = Substitute.For<ILogger>();

			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest)
				.Returns("http://localhost/entity-schema-manager");
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
				.Returns("http://localhost/runtime-entity-schema");

			applicationClient.ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns("{\"collection\":[{\"name\":\"Account\"},{\"name\":\"AccountFile\"}]}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"Account\"}",
					Arg.Any<int>(),
					Arg.Any<int>(),
					Arg.Any<int>())
				.Returns("{\"schema\":{\"columns\":{\"Items\":{}}}}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"AccountFile\"}",
					Arg.Any<int>(),
					Arg.Any<int>(),
					Arg.Any<int>())
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
				serviceUrlBuilder,
				logger);

			// Act
			modelBuilder.GetModels(options);

			// Assert
			string accountModelText = File.ReadAllText(Path.Combine(tempPath, "Account.cs"));
			accountModelText.Should().Contain("#region Details",
				because: "the master model should expose a dedicated details region when reverse detail relationships exist");
			accountModelText.Should().Contain("[DetailProperty(nameof(global::CreatioModel.AccountFile.AccountId))]",
				because: "the generated reverse detail should point at the lookup property on the detail schema");
			accountModelText.Should().Contain("public virtual List<AccountFile> CollectionOfAccountFileByAccount { get; set; }",
				because: "the generated master model should expose the reverse detail collection property");
			accountModelText.Should().Contain("#endregion",
				because: "the generated details region should close cleanly after reverse detail properties");

			string accountFileModelText = File.ReadAllText(Path.Combine(tempPath, "AccountFile.cs"));
			accountFileModelText.Should().Contain("[LookupProperty(\"Account\")]",
				because: "the detail schema should keep its lookup metadata when the reverse detail is generated");
			accountFileModelText.Should().Contain("public virtual Account Account { get; set; }",
				because: "the detail schema should keep the lookup navigation property");
		}
		finally {
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
		}
	}

	[Test]
	[Description("Skips reverse detail generation when the lookup column itself is named Id so the generator does not create invalid IdId or detail collection members.")]
	public void GetModels_DoesNotGenerateReverseDetailForLookupNamedId()
	{
		// Arrange
		string tempPath = Path.Combine(Path.GetTempPath(), $"clio-modelbuilder-tests-{Guid.NewGuid():N}");
		try {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
			ILogger logger = Substitute.For<ILogger>();

			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.EntitySchemaManagerRequest)
				.Returns("http://localhost/entity-schema-manager");
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
				.Returns("http://localhost/runtime-entity-schema");

			applicationClient.ExecutePostRequest("http://localhost/entity-schema-manager", string.Empty, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns("{\"collection\":[{\"name\":\"Master\"},{\"name\":\"Detail\"}]}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"Master\"}",
					Arg.Any<int>(),
					Arg.Any<int>(),
					Arg.Any<int>())
				.Returns("{\"schema\":{\"columns\":{\"Items\":{}}}}");
			applicationClient.ExecutePostRequest("http://localhost/runtime-entity-schema",
					"{\"Name\" : \"Detail\"}",
					Arg.Any<int>(),
					Arg.Any<int>(),
					Arg.Any<int>())
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
				serviceUrlBuilder,
				logger);

			// Act
			modelBuilder.GetModels(options);

			// Assert
			string masterModelText = File.ReadAllText(Path.Combine(tempPath, "Master.cs"));
			masterModelText.Should().NotContain("IdId",
				because: "a lookup column already named Id should not be expanded into an invalid IdId property");
			masterModelText.Should().NotContain("CollectionOfDetail",
				because: "the generator should not create a reverse detail from a lookup column named Id");
			masterModelText.Should().NotContain("#region Details",
				because: "the details region should be omitted when no valid reverse detail exists");
		}
		finally {
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
		}
	}
}
