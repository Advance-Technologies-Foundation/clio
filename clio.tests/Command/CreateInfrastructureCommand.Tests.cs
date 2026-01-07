using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
public class CreateInfrastructureCommandTests : BaseCommandTests<CreateInfrastructureOptions>
{
	private IFileSystem _fileSystem;
	private CreateInfrastructureCommand _command;

	[SetUp]
	public override void Setup()
	{
		base.Setup(); // Call base setup first
		_fileSystem = Substitute.For<IFileSystem>();
		_command = new CreateInfrastructureCommand(_fileSystem);
	}

	private static CreateInfrastructureOptions CreateOptionsWithDefaults()
	{
		var options = new CreateInfrastructureOptions();
		var properties = typeof(CreateInfrastructureOptions).GetProperties();
		
		foreach (var property in properties)
		{
			var optionAttribute = property.GetCustomAttribute<OptionAttribute>();
			if (optionAttribute?.Default != null)
			{
				property.SetValue(options, optionAttribute.Default);
			}
		}
		
		return options;
	}

	[Test]
	[Description("Execute should copy infrastructure files from template directory to destination")]
	public void Execute_ShouldCopyFilesFromTemplateToDest()
	{
		// Arrange
		var options = CreateOptionsWithDefaults();
		
		// Act
		_command.Execute(options);
		
		// Assert
		_fileSystem.Received(1).CopyDirectory(
			Arg.Any<string>(), 
			Arg.Any<string>(), 
			true);
	}

	[Test]
	[Description("Execute should process PostgreSQL StatefulSet with default resource values")]
	public void Execute_ShouldProcessPostgresStatefulSetWithDefaultValues()
	{
		// Arrange
		var options = CreateOptionsWithDefaults();
		var templateContent = @"
resources:
  limits:
    memory: ""{{PG_LIMIT_MEMORY}}""
    cpu: ""{{PG_LIMIT_CPU}}""
  requests:
    memory: ""{{PG_REQUEST_MEMORY}}""
    cpu: ""{{PG_REQUEST_CPU}}""";
		
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(templateContent);
		
		// Act
		_command.Execute(options);
		
		// Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")),
			Arg.Is<string>(content => 
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"") &&
				content.Contains("memory: \"2Gi\"") &&
				content.Contains("cpu: \"1\"")));
	}

	[Test]
	[Description("Execute should process MSSQL StatefulSet with default resource values")]
	public void Execute_ShouldProcessMssqlStatefulSetWithDefaultValues()
	{
		// Arrange
		var options = CreateOptionsWithDefaults();
		var templateContent = @"
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
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")),
			Arg.Is<string>(content => 
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"") &&
				content.Contains("memory: \"2Gi\"") &&
				content.Contains("cpu: \"1\"")));
	}

	[Test]
	[Description("Execute should use custom PostgreSQL resource values when provided")]
	public void Execute_ShouldUseCustomPostgresResourceValues()
	{
		// Arrange
		var options = new CreateInfrastructureOptions
		{
			PostgresLimitMemory = "8Gi",
			PostgresLimitCpu = "4",
			PostgresRequestMemory = "4Gi",
			PostgresRequestCpu = "2"
		};
		
		var templateContent = @"
resources:
  limits:
    memory: ""{{PG_LIMIT_MEMORY}}""
    cpu: ""{{PG_LIMIT_CPU}}""
  requests:
    memory: ""{{PG_REQUEST_MEMORY}}""
    cpu: ""{{PG_REQUEST_CPU}}""";
		
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(templateContent);
		
		// Act
		_command.Execute(options);
		
		// Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")),
			Arg.Is<string>(content => 
				content.Contains("memory: \"8Gi\"") &&
				content.Contains("cpu: \"4\"") &&
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"")));
	}

	[Test]
	[Description("Execute should use custom MSSQL resource values when provided")]
	public void Execute_ShouldUseCustomMssqlResourceValues()
	{
		// Arrange
		var options = new CreateInfrastructureOptions
		{
			MssqlLimitMemory = "8Gi",
			MssqlLimitCpu = "4",
			MssqlRequestMemory = "4Gi",
			MssqlRequestCpu = "2"
		};
		
		var templateContent = @"
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
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")),
			Arg.Is<string>(content => 
				content.Contains("memory: \"8Gi\"") &&
				content.Contains("cpu: \"4\"") &&
				content.Contains("memory: \"4Gi\"") &&
				content.Contains("cpu: \"2\"")));
	}

	[Test]
	[Description("Execute should handle PostgreSQL file not existing gracefully")]
	public void Execute_ShouldHandlePostgresFileNotExisting()
	{
		// Arrange
		var options = CreateOptionsWithDefaults();
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")))
			.Returns(false);
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")))
			.Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("content");
		
		// Act
		var result = _command.Execute(options);
		
		// Assert
		result.Should().Be(0, because: "command should complete successfully even if PostgreSQL file doesn't exist");
		_fileSystem.DidNotReceive().WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")),
			Arg.Any<string>());
	}

	[Test]
	[Description("Execute should handle MSSQL file not existing gracefully")]
	public void Execute_ShouldHandleMssqlFileNotExisting()
	{
		// Arrange
		var options = CreateOptionsWithDefaults();
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("postgres-stateful-set.yaml")))
			.Returns(true);
		_fileSystem.ExistsFile(Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")))
			.Returns(false);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("content");
		
		// Act
		var result = _command.Execute(options);
		
		// Assert
		result.Should().Be(0, because: "command should complete successfully even if MSSQL file doesn't exist");
		_fileSystem.DidNotReceive().WriteAllTextToFile(
			Arg.Is<string>(path => path.Contains("mssql-stateful-set.yaml")),
			Arg.Any<string>());
	}

	[Test]
	[Description("Execute should replace all placeholders in template content")]
	public void Execute_ShouldReplaceAllPlaceholders()
	{
		// Arrange
		var options = new CreateInfrastructureOptions
		{
			PostgresLimitMemory = "16Gi",
			PostgresLimitCpu = "8",
			PostgresRequestMemory = "8Gi",
			PostgresRequestCpu = "4"
		};
		
		var templateContent = @"
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
	public void Execute_ShouldReturnSuccessCode()
	{
		// Arrange
		var options = CreateOptionsWithDefaults();
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("content");
		
		// Act
		var result = _command.Execute(options);
		
		// Assert
		result.Should().Be(0, because: "successful execution should return 0");
	}

	[Test]
	[Description("Execute should handle memory values with Mi suffix")]
	public void Execute_ShouldHandleMemoryValuesWithMiSuffix()
	{
		// Arrange
		var options = new CreateInfrastructureOptions
		{
			PostgresRequestMemory = "512Mi",
			MssqlRequestMemory = "256Mi"
		};
		
		var templateContent = "{{PG_REQUEST_MEMORY}} {{MSSQL_REQUEST_MEMORY}}";
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
	[Description("Execute should handle decimal CPU values")]
	public void Execute_ShouldHandleDecimalCpuValues()
	{
		// Arrange
		var options = new CreateInfrastructureOptions
		{
			PostgresRequestCpu = "0.5",
			MssqlRequestCpu = "0.25"
		};
		
		var templateContent = "{{PG_REQUEST_CPU}} {{MSSQL_REQUEST_CPU}}";
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
}
