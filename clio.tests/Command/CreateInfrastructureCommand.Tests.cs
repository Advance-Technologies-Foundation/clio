using System.Reflection;
using Clio.Command;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class CreateInfrastructureCommandTests : BaseCommandTests<CreateInfrastructureOptions>{
	#region Fields: Private

	private CreateInfrastructureCommand _command;
	private IFileSystem _fileSystem;
	private IInfrastructurePathProvider _infrastructurePathProvider;
	private ILogger _logger;

	#endregion

	#region Methods: Private

	private static CreateInfrastructureOptions CreateOptionsWithDefaults() {
		CreateInfrastructureOptions options = new();
		PropertyInfo[] properties = typeof(CreateInfrastructureOptions).GetProperties();

		foreach (PropertyInfo property in properties) {
			OptionAttribute optionAttribute = property.GetCustomAttribute<OptionAttribute>();
			if (optionAttribute?.Default != null) {
				property.SetValue(options, optionAttribute.Default);
			}
		}

		return options;
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);

		_fileSystem = Substitute.For<IFileSystem>();
		containerBuilder.AddTransient(_ => _fileSystem);

		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _logger);

		_infrastructurePathProvider = Substitute.For<IInfrastructurePathProvider>();
		_infrastructurePathProvider
			.GetInfrastructurePath(Arg.Any<string>())
			.Returns("test-infrastructure-path");
		containerBuilder.AddTransient(_ => _infrastructurePathProvider);
	}

	#endregion

	#region Methods: Public

	[Test]
	[Description("Execute should copy infrastructure files from template directory to destination")]
	public void Execute_ShouldCopyFilesFromTemplateToDest() {
		// Arrange
		CreateInfrastructureOptions options = CreateOptionsWithDefaults();

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received(1).CopyDirectory(
			Arg.Any<string>(),
			Arg.Any<string>(),
			true);
	}

	[Test]
	[Description("Execute should handle decimal CPU values")]
	public void Execute_ShouldHandleDecimalCpuValues() {
		// Arrange
		CreateInfrastructureOptions options = new() {
			PostgresRequestCpu = "0.5",
			MssqlRequestCpu = "0.25"
		};

		string templateContent = "{{PG_REQUEST_CPU}} {{MSSQL_REQUEST_CPU}}";
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received().WriteAllTextToFile(
			Arg.Any<string>(),
			Arg.Is<string>(content =>
				content.Contains("0.5") &&
				content.Contains("0.25")));
	}

	[Test]
	[Description("Execute should handle memory values with Mi suffix")]
	public void Execute_ShouldHandleMemoryValuesWithMiSuffix() {
		// Arrange
		CreateInfrastructureOptions options = new() {
			PostgresRequestMemory = "512Mi",
			MssqlRequestMemory = "256Mi"
		};

		string templateContent = "{{PG_REQUEST_MEMORY}} {{MSSQL_REQUEST_MEMORY}}";
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received().WriteAllTextToFile(
			Arg.Any<string>(),
			Arg.Is<string>(content =>
				content.Contains("512Mi") &&
				content.Contains("256Mi")));
	}

	[Test]
	[Description("Execute should handle MSSQL file not existing gracefully")]
	public void Execute_ShouldHandleMssqlFileNotExisting() {
		// Arrange
		CreateInfrastructureOptions options = CreateOptionsWithDefaults();
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")))
				   .Returns(true);
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")))
				   .Returns(false);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("content");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should complete successfully even if MSSQL file doesn't exist");
		_fileSystem.DidNotReceive().WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")),
			Arg.Any<string>());
	}

	[Test]
	[Description("Execute should handle PostgreSQL file not existing gracefully")]
	public void Execute_ShouldHandlePostgresFileNotExisting() {
		// Arrange
		CreateInfrastructureOptions options = CreateOptionsWithDefaults();
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")))
				   .Returns(false);
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")))
				   .Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("content");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "command should complete successfully even if PostgreSQL file doesn't exist");
		_fileSystem.DidNotReceive().WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")),
			Arg.Any<string>());
	}

	[Test]
	[Description("Execute should process MSSQL StatefulSet with default resource values")]
	public void Execute_ShouldProcessMssqlStatefulSetWithDefaultValues() {
		// Arrange
		const string yamlFileName = "mssql-stateful-set.yaml";
		CreateInfrastructureOptions options = CreateOptionsWithDefaults();
		const string templateContent = @"
resources:
  limits:
    memory: ""{{MSSQL_LIMIT_MEMORY}}""
    cpu: ""{{MSSQL_LIMIT_CPU}}""
  requests:
    memory: ""{{MSSQL_REQUEST_MEMORY}}""
    cpu: ""{{MSSQL_REQUEST_CPU}}""";

		_fileSystem.NormalizeFilePathByPlatform(Arg.Any<string>()).Returns(args => args[0].ToString());
		_fileSystem.ExistsFile(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(true);
		_fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains(yamlFileName)),
			Arg.Is<string>(content =>
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"") &&
				content.Contains("memory: \"2Gi\"") &&
				content.Contains("cpu: \"1\"")));
	}

	[Test]
	[Description("Execute should process PostgreSQL StatefulSet with default resource values")]
	public void Execute_ShouldProcessPostgresStatefulSetWithDefaultValues() {
		// Arrange
		const string yamlFileName = "postgres-stateful-set.yaml";
		CreateInfrastructureOptions options = CreateOptionsWithDefaults();
		const string templateContent = @"
resources:
  limits:
    memory: ""{{PG_LIMIT_MEMORY}}""
    cpu: ""{{PG_LIMIT_CPU}}""
  requests:
    memory: ""{{PG_REQUEST_MEMORY}}""
    cpu: ""{{PG_REQUEST_CPU}}""";

		_fileSystem.NormalizeFilePathByPlatform(Arg.Any<string>()).Returns(args => args[0].ToString());
		_fileSystem.ExistsFile(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(true);
		_fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains(yamlFileName)),
			Arg.Is<string>(content =>
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"") &&
				content.Contains("memory: \"2Gi\"") &&
				content.Contains("cpu: \"1\"")));
	}

	[Test]
	[Description("Execute should replace all placeholders in template content")]
	public void Execute_ShouldReplaceAllPlaceholders() {
		// Arrange
		CreateInfrastructureOptions options = new() {
			PostgresLimitMemory = "16Gi",
			PostgresLimitCpu = "8",
			PostgresRequestMemory = "8Gi",
			PostgresRequestCpu = "4"
		};

		string templateContent = @"
postgres:
  resources:
    limits:
      memory: ""{{PG_LIMIT_MEMORY}}""
      cpu: ""{{PG_LIMIT_CPU}}""
    requests:
      memory: ""{{PG_REQUEST_MEMORY}}""
      cpu: ""{{PG_REQUEST_CPU}}""
mssql:
  resources:
    limits:
      memory: ""{{MSSQL_LIMIT_MEMORY}}""
      cpu: ""{{MSSQL_LIMIT_CPU}}""
    requests:
      memory: ""{{MSSQL_REQUEST_MEMORY}}""
      cpu: ""{{MSSQL_REQUEST_CPU}}""";

		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received().WriteAllTextToFile(
			Arg.Any<string>(),
			Arg.Is<string>(content =>
				!content.Contains("{{PG_LIMIT_MEMORY}}") &&
				!content.Contains("{{PG_LIMIT_CPU}}") &&
				!content.Contains("{{PG_REQUEST_MEMORY}}") &&
				!content.Contains("{{PG_REQUEST_CPU}}") &&
				!content.Contains("{{MSSQL_LIMIT_MEMORY}}") &&
				!content.Contains("{{MSSQL_LIMIT_CPU}}") &&
				!content.Contains("{{MSSQL_REQUEST_MEMORY}}") &&
				!content.Contains("{{MSSQL_REQUEST_CPU}}")));
	}

	[Test]
	[Description("Execute should return success code 0")]
	public void Execute_ShouldReturnSuccessCode() {
		// Arrange
		CreateInfrastructureOptions options = CreateOptionsWithDefaults();
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("content");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "successful execution should return 0");
	}

	[Test]
	[Description("Execute should use custom MSSQL resource values when provided")]
	public void Execute_ShouldUseCustomMssqlResourceValues() {
		// Arrange
		const string yamlFileName = "mssql-stateful-set.yaml";
		CreateInfrastructureOptions options = new() {
			MssqlLimitMemory = "8Gi",
			MssqlLimitCpu = "4",
			MssqlRequestMemory = "4Gi",
			MssqlRequestCpu = "2"
		};

		const string templateContent = @"
resources:
  limits:
    memory: ""{{MSSQL_LIMIT_MEMORY}}""
    cpu: ""{{MSSQL_LIMIT_CPU}}""
  requests:
    memory: ""{{MSSQL_REQUEST_MEMORY}}""
    cpu: ""{{MSSQL_REQUEST_CPU}}""";

		_fileSystem.NormalizeFilePathByPlatform(Arg.Any<string>()).Returns(args => args[0].ToString());
		_fileSystem.ExistsFile(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(true);
		_fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains(yamlFileName)),
			Arg.Is<string>(content =>
				content.Contains("memory: \"8Gi\"") &&
				content.Contains("cpu: \"4\"") &&
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"")));
	}

	[Test]
	[Description("Execute should use custom PostgreSQL resource values when provided")]
	public void Execute_ShouldUseCustomPostgresResourceValues() {
		// Arrange
		const string yamlFileName = "postgres-stateful-set.yaml";
		CreateInfrastructureOptions options = new() {
			PostgresLimitMemory = "8Gi",
			PostgresLimitCpu = "4",
			PostgresRequestMemory = "4Gi",
			PostgresRequestCpu = "2"
		};

		const string templateContent = @"
resources:
  limits:
    memory: ""{{PG_LIMIT_MEMORY}}""
    cpu: ""{{PG_LIMIT_CPU}}""
  requests:
    memory: ""{{PG_REQUEST_MEMORY}}""
    cpu: ""{{PG_REQUEST_CPU}}""";

		_fileSystem.NormalizeFilePathByPlatform(Arg.Any<string>()).Returns(args => args[0].ToString());
		_fileSystem.ExistsFile(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(true);
		_fileSystem.ReadAllText(Arg.Is<string>(p => p.EndsWith(yamlFileName))).Returns(templateContent);

		// Act
		_command.Execute(options);

		// Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains(yamlFileName)),
			Arg.Is<string>(content =>
				content.Contains("memory: \"8Gi\"") &&
				content.Contains("cpu: \"4\"") &&
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"")));
	}


	public override void Setup() {
		base.Setup(); // Call base setup first
		_command = Container.GetRequiredService<CreateInfrastructureCommand>();
	}

	public override void TearDown() {
		_fileSystem.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	#endregion
}
