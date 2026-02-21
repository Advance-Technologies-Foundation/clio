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
				options,
				workingDirectoriesProvider,
				serviceUrlBuilder);

			// Act
			modelBuilder.GetModels();

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
}
