using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class BuildDockerImageServiceTests {
	private System.IO.Abstractions.FileSystem _msFileSystem = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private ILogger _logger = null!;
	private IProcessExecutor _processExecutor = null!;
	private ISettingsRepository _settingsRepository = null!;
	private IDockerTemplatePathProvider _templatePathProvider = null!;
	private IZipFile _zipFile = null!;
	private BuildDockerImageService _service = null!;
	private string _tempRoot = string.Empty;

	[SetUp]
	public void Setup() {
		_msFileSystem = new System.IO.Abstractions.FileSystem();
		_fileSystem = new Clio.Common.FileSystem(_msFileSystem);
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetContainerImageCli().Returns("docker");
		_templatePathProvider = Substitute.For<IDockerTemplatePathProvider>();
		_zipFile = Substitute.For<IZipFile>();
		_service = new BuildDockerImageService(_processExecutor, _logger, _settingsRepository, _fileSystem, _msFileSystem, _zipFile,
			_templatePathProvider);
		_tempRoot = _msFileSystem.Path.Combine(_msFileSystem.Path.GetTempPath(), "clio-build-docker-image-tests",
			Guid.NewGuid().ToString("N"));
		_msFileSystem.Directory.CreateDirectory(_tempRoot);
	}

	[TearDown]
	public void TearDown() {
		_processExecutor.ClearReceivedCalls();
		_templatePathProvider.ClearReceivedCalls();
		_zipFile.ClearReceivedCalls();
		if (_msFileSystem.Directory.Exists(_tempRoot)) {
			_msFileSystem.Directory.Delete(_tempRoot, true);
		}
	}

	[Test]
	[Description("Execute should build a local Docker image from a .NET 8+ directory source using the resolved template.")]
	public void Execute_ShouldBuildImageFromDirectorySource() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("My Source");
		string templateDirectory = CreateTemplateDirectory("dev-template");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the docker version check and image build both succeeded");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker" && o.Arguments == "--version"));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker"
			&& o.Arguments.Contains("build --label \"org.creatio.database-source=My Source\" -t \"creatio-dev:my_source\"")));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("save --output")));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("push \"")));
	}

	[Test]
	[Description("Execute should use nerdctl with the k8s.io namespace when appsettings selects nerdctl.")]
	public void Execute_ShouldUseNerdctlWithK8sNamespace_WhenConfiguredInSettings() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Nerdctl Source");
		string templateDirectory = CreateTemplateDirectory("dev-template");
		_settingsRepository.GetContainerImageCli().Returns("nerdctl");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because nerdctl was selected from appsettings and all commands succeeded");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "nerdctl" && o.Arguments == "--namespace k8s.io --version"));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "nerdctl" && o.Arguments.Contains("--namespace k8s.io build")));
	}

	[Test]
	[Description("Execute should let the CLI override a nerdctl appsettings default and force docker instead.")]
	public void Execute_ShouldPreferUseDockerOverSettingsDefault() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Override Source");
		string templateDirectory = CreateTemplateDirectory("prod-template");
		_settingsRepository.GetContainerImageCli().Returns("nerdctl");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			UseDocker = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the explicit --use-docker flag should override appsettings");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker" && o.Arguments == "--version"));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "nerdctl"));
	}

	[Test]
	[Description("Execute should stream Docker output as undecorated lines instead of mirroring stderr as logger errors.")]
	public void Execute_ShouldStreamDockerOutputWithWriteLineCallback() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Output Style");
		string templateDirectory = CreateTemplateDirectory("dev-template");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions options = callInfo.Arg<ProcessExecutionOptions>();
				options.OnOutput?.Invoke("#0 building with \"default\" instance using docker driver", ProcessOutputStream.StdErr);
				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the docker calls completed successfully");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.MirrorOutputToLogger == false && o.OnOutput != null));
		_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("#0 building with", StringComparison.Ordinal)));
		_logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("#0 building with", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should extract ZIP sources and then save and push the built image when output-path and registry are provided.")]
	public void Execute_ShouldSaveAndPushImageWhenOutputPathAndRegistryProvided() {
		// Arrange
		string sourceArchive = _msFileSystem.Path.Combine(_tempRoot, "8.3.3_StudioNet8.zip");
		_msFileSystem.File.WriteAllText(sourceArchive, "zip-placeholder");
		string templateDirectory = CreateTemplateDirectory("prod-template");
		string outputTarPath = _msFileSystem.Path.Combine(_tempRoot, "out", "image.tar");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_zipFile.When(z => z.ExtractToDirectory(sourceArchive, Arg.Any<string>()))
			.Do(callInfo => {
				string destination = callInfo.ArgAt<string>(1);
				string extractedRoot = _msFileSystem.Path.Combine(destination, "app");
				_msFileSystem.Directory.CreateDirectory(extractedRoot);
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedRoot, "Terrasoft.WebHost.dll"), "dll");
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedRoot, "Terrasoft.WebHost.dll.config"), "config");
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedRoot, "appsettings.json"), "{}");
			});
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceArchive,
			Template = "prod",
			OutputPath = outputTarPath,
			Registry = "ghcr.io/acme"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the extracted source, build, save, tag, and push all succeeded");
		_zipFile.Received(1).ExtractToDirectory(sourceArchive, Arg.Any<string>());
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("save --output") && o.Arguments.Contains("creatio-prod:8.3.3_studionet8")));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("tag \"creatio-prod:8.3.3_studionet8\" \"ghcr.io/acme/creatio-prod:8.3.3_studionet8\"")));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("push \"ghcr.io/acme/creatio-prod:8.3.3_studionet8\"")));
		_msFileSystem.Directory.Exists(_msFileSystem.Path.GetDirectoryName(outputTarPath) ?? string.Empty).Should().BeTrue(
			"because the command should create the output directory before docker save runs");
	}

	[Test]
	[Description("Execute should reject .NET Framework payloads before trying to invoke Docker.")]
	public void Execute_ShouldRejectNetFrameworkSource() {
		// Arrange
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, "framework-app");
		_msFileSystem.Directory.CreateDirectory(sourceDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Web.config"), "<configuration />");
		string templateDirectory = CreateTemplateDirectory("prod-template");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because .NET Framework payloads are not supported for Docker builds");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains(".NET Framework", StringComparison.Ordinal)));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>());
	}

	[Test]
	[Description("Execute should fail with a clear error when Docker is not available in PATH.")]
	public void Execute_ShouldFailWhenDockerVersionCheckCannotStart() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Docker Missing");
		string templateDirectory = CreateTemplateDirectory("dev-template");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = false,
				ExitCode = 1,
				StandardError = "docker executable not found"
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because docker is required to build container images");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Container image CLI 'docker' is not installed", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should reject mutually exclusive CLI engine overrides before starting the build.")]
	public void Execute_ShouldRejectConflictingCliOverrides() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Conflicting Overrides");
		string templateDirectory = CreateTemplateDirectory("dev-template");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", templateDirectory, true));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev",
			UseDocker = true,
			UseNerdctl = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because both engine override flags cannot be used together");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Use either --use-docker or --use-nerdctl", StringComparison.Ordinal)));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>());
	}

	private string CreateDotNetSourceDirectory(string leafName) {
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		_msFileSystem.Directory.CreateDirectory(sourceDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Terrasoft.WebHost.dll"), "dll");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Terrasoft.WebHost.dll.config"), "config");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "appsettings.json"), "{}");
		return sourceDirectory;
	}

	private string CreateTemplateDirectory(string leafName) {
		string templateDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		_msFileSystem.Directory.CreateDirectory(templateDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(templateDirectory, "Dockerfile"),
			"FROM scratch\nCOPY source/ ./\n");
		return templateDirectory;
	}
}
