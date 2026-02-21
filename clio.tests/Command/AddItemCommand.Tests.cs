using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.Project;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
internal class AddItemCommandTests : BaseCommandTests<AddItemOptions>{
	#region Fields: Private

	private IApplicationClient _applicationClient;
	private AddItemCommand _command;
	private ILogger _logger;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IVsProject _vsProject;
	private IVsProjectFactory _vsProjectFactory;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;

	#endregion

	#region Methods: Public

	[Test]
	[Category("Unit")]
	public void Execute_ModelSingle_CallsUnderlyingServicesAndProject() {
		// Arrange


		AddItemOptions options = new() {
			ItemType = "model",
			CreateAll = false,
			ItemName = "Contact",
			Fields = "Name,Email",
			Namespace = "Codex",
			DestinationPath = @"C:\Models"
		};
		const string expectedUrl = "http://localhost/rest/CreatioApiGateway/GetEntitySchemaModels/Contact/Name,Email";
		_serviceUrlBuilder.Build("/rest/CreatioApiGateway/GetEntitySchemaModels/Contact/Name,Email")
						  .Returns(expectedUrl);
		_applicationClient.ExecuteGetRequest(expectedUrl).Returns("{\"Contact\":\"class Contact {}\"}");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_serviceUrlBuilder.Received(1).Build("/rest/CreatioApiGateway/GetEntitySchemaModels/Contact/Name,Email");
		_applicationClient.Received(1).ExecuteGetRequest(expectedUrl);
		_vsProjectFactory.Received(1).Create(@"C:\Models", "Codex");
		_vsProject.Received(1).AddFile("Contact", "class Contact {}");
		_vsProject.Received(1).Reload();
		_logger.Received(1).WriteInfo("Done");
	}

	[Test]
	[Category("Unit")]
	public void Execute_ReturnsError_WhenOptionsInvalid() {
		// Arrange
		AddItemOptions options = new() {
			ItemType = "model",
			CreateAll = false
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1);
		_logger.Received().WriteError("Namespace is required for model generation.");
		_logger.Received().WriteError("Item name is required.");
		_serviceUrlBuilder.Received(0).Build(Arg.Any<string>());
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default);
		_vsProjectFactory.DidNotReceiveWithAnyArgs().Create();
	}

	[Test]
	[Category("Unit")]
	public void Execute_TemplateItem_CallsUnderlyingProjectWithTemplateBody() {
		// Arrange
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string tempDirectory = Path.Combine(Path.GetTempPath(), $"add-item-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(tempDirectory, "tpl"));
		File.WriteAllText(Path.Combine(tempDirectory, "tpl", "service-template.tpl"), "public class <Name> {}");

		try {
			Environment.CurrentDirectory = tempDirectory;
			AddItemOptions options = new() {
				ItemType = "service",
				ItemName = "MyService",
				Namespace = "Codex",
				DestinationPath = @"C:\Models"
			};

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0);
			_vsProjectFactory.Received(1).Create(@"C:\Models", "Codex");
			_vsProject.Received(1).AddFile("MyService", "public class MyService {}");
			_vsProject.Received(1).Reload();
		}
		finally {
			Environment.CurrentDirectory = originalCurrentDirectory;
			if (Directory.Exists(tempDirectory)) {
				Directory.Delete(tempDirectory, true);
			}
		}
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_vsProjectFactory = Substitute.For<IVsProjectFactory>();
		_vsProject = Substitute.For<IVsProject>();
		_logger = Substitute.For<ILogger>();
		_vsProjectFactory.Create(Arg.Any<string>(), Arg.Any<string>()).Returns(_vsProject);
		_command = new AddItemCommand(
			_applicationClient,
			_serviceUrlBuilder,
			_workingDirectoriesProvider,
			new AddItemOptionsValidator(),
			_vsProjectFactory,
			_logger);
	}

	#endregion
}
