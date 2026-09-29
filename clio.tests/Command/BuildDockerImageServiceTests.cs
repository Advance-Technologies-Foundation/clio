using System;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class BuildDockerImageServiceTests {
	private ICodeServerArchiveCache _codeServerArchiveCache = null!;
	private IContainerRegistryPreflightService _containerRegistryPreflightService = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private ILogger _logger = null!;
	private System.IO.Abstractions.FileSystem _msFileSystem = null!;
	private IProcessExecutor _processExecutor = null!;
	private ISettingsRepository _settingsRepository = null!;
	private BuildDockerImageService _service = null!;
	private IDockerTemplatePathProvider _templatePathProvider = null!;
	private string _tempRoot = string.Empty;
	private IZipFile _zipFile = null!;
	private string _cachedCodeServerArchivePath = string.Empty;

	[SetUp]
	public void Setup() {
		_msFileSystem = new System.IO.Abstractions.FileSystem();
		_fileSystem = new Clio.Common.FileSystem(_msFileSystem);
		_codeServerArchiveCache = Substitute.For<ICodeServerArchiveCache>();
		_containerRegistryPreflightService = Substitute.For<IContainerRegistryPreflightService>();
		_containerRegistryPreflightService.ValidatePushTarget(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new ContainerRegistryPreflightResult(true, "https://registry.example/", "ok"));
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetContainerImageCli().Returns("docker");
		_templatePathProvider = Substitute.For<IDockerTemplatePathProvider>();
		_zipFile = Substitute.For<IZipFile>();
		_tempRoot = _msFileSystem.Path.Combine(_msFileSystem.Path.GetTempPath(), "clio-build-docker-image-tests",
			Guid.NewGuid().ToString("N"));
		_msFileSystem.Directory.CreateDirectory(_tempRoot);
		_cachedCodeServerArchivePath = _msFileSystem.Path.Combine(_tempRoot, "code-server.tar.gz");
		_msFileSystem.File.WriteAllText(_cachedCodeServerArchivePath, "code-server");
		_codeServerArchiveCache.EnsureArchiveAvailable(Arg.Any<string>()).Returns(_cachedCodeServerArchivePath);
		_settingsRepository.AppSettingsFilePath.Returns(_msFileSystem.Path.Combine(_tempRoot, "settings", "appsettings.json"));
		_service = new BuildDockerImageService(_processExecutor, _codeServerArchiveCache, _containerRegistryPreflightService, _logger, _settingsRepository,
			_fileSystem, _msFileSystem, _zipFile, _templatePathProvider);
	}

	[TearDown]
	public void TearDown() {
		_processExecutor.ClearReceivedCalls();
		_codeServerArchiveCache.ClearReceivedCalls();
		_containerRegistryPreflightService.ClearReceivedCalls();
		_templatePathProvider.ClearReceivedCalls();
		_zipFile.ClearReceivedCalls();
		if (_msFileSystem.Directory.Exists(_tempRoot)) {
			_msFileSystem.Directory.Delete(_tempRoot, true);
		}
	}

	[Test]
	[Description("Execute should autodetect docker first when no CLI override is supplied and docker is available.")]
	public void Execute_ShouldAutodetectDockerWhenAvailable() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Program == "docker" && executionOptions.Arguments == "info") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			Template = "base"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because docker should be selected automatically when it responds successfully");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker" && o.Arguments == "info"));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "nerdctl" && o.Arguments == "--namespace k8s.io info"));
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Program == "docker" && o.Arguments.Contains("build --pull=false", StringComparison.Ordinal),
			"because the build should continue with docker after successful runtime detection");
	}

	[Test]
	[Description("Execute should autodetect nerdctl when docker probing fails and nerdctl is available.")]
	public void Execute_ShouldAutodetectNerdctlWhenDockerProbeFails() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Program == "docker" && executionOptions.Arguments == "info") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1
					});
				}

				if (executionOptions.Program == "nerdctl" && executionOptions.Arguments == "--namespace k8s.io info") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			Template = "base"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because nerdctl should be selected when docker is unavailable but nerdctl works");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker" && o.Arguments == "info"));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "nerdctl" && o.Arguments == "--namespace k8s.io info"));
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Program == "nerdctl" && o.Arguments.Contains("--namespace k8s.io build --pull=false", StringComparison.Ordinal),
			"because the build should continue with nerdctl after docker probing fails");
	}

	[Test]
	[Description("Execute should fail with a clear error when neither docker nor nerdctl is available at runtime.")]
	public void Execute_ShouldFailWhenNoContainerImageCliCanBeDetected() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if ((executionOptions.Program == "docker" && executionOptions.Arguments == "info")
					|| (executionOptions.Program == "nerdctl" && executionOptions.Arguments == "--namespace k8s.io info")) {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			Template = "base"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because the command cannot proceed without either docker or nerdctl");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Could not detect an available container image CLI", StringComparison.Ordinal)
			&& s.Contains("docker info", StringComparison.Ordinal)
			&& s.Contains("nerdctl info", StringComparison.Ordinal)));
		GetReceivedExecutionOptions().Should().NotContain(o =>
				o.Arguments.Contains("build --pull=false", StringComparison.Ordinal),
			"because the image build must not start when neither container CLI is available");
	}

	[Test]
	[Description("Execute should build the base template without requiring a Creatio source path and use the default base image reference.")]
	public void Execute_ShouldBuildBaseTemplateWithoutSourcePath() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			Template = "base"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because template base should build from its own Dockerfile without packaging a Creatio distribution");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Program == "docker"
				&& o.Arguments.Contains("build --pull=false -t \"creatio-base:8.0-v1\"", StringComparison.Ordinal)
				&& !o.Arguments.Contains("--label", StringComparison.Ordinal)
				&& o.WorkingDirectory != null,
			"because template base should build without source labels and with a concrete Docker build context");
	}

	[Test]
	[Description("Execute should allow template base to build a caller-specified base image reference through --base-image.")]
	public void Execute_ShouldUseCustomBaseImageReferenceForBaseTemplate() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			Template = "base",
			BaseImage = "ghcr.io/acme/creatio-base:dotnet10-vpn"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because template base should build whichever explicit base image reference the caller requested");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker"
			&& o.Arguments.Contains("build --pull=false -t \"ghcr.io/acme/creatio-base:dotnet10-vpn\"")));
	}

	[Test]
	[Description("Execute should cache the bundled base image as a reusable archive after a successful base build.")]
	public void Execute_ShouldCacheBaseImageArchiveAfterBaseBuild() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			Template = "base"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because a successful bundled base build should also persist a reusable local archive");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Program == "docker"
				&& o.Arguments.Contains("save --output", StringComparison.Ordinal)
				&& o.Arguments.Contains("\"creatio-base:8.0-v1\"", StringComparison.Ordinal),
			"because clio should cache the built bundled base image as a local archive for later restore");
	}

	[Test]
	[Description("Execute should not prepend --registry when template base already builds a fully-qualified image reference through --base-image.")]
	public void Execute_ShouldReuseQualifiedBaseImageReferenceWhenRegistryIsAlsoProvided() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			Template = "base",
			BaseImage = "ghcr.io/acme/creatio-base:dotnet10-vpn",
			Registry = "docker.io"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0,
			"because a fully-qualified base image reference should remain the tag/push target even when --registry is also supplied");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "tag \"ghcr.io/acme/creatio-base:dotnet10-vpn\" \"ghcr.io/acme/creatio-base:dotnet10-vpn\""));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "push \"ghcr.io/acme/creatio-base:dotnet10-vpn\""));
	}

	[Test]
	[Description("Execute should treat a base image reference with a registry port as already qualified and preflight the effective push target instead of the raw --registry option.")]
	public void Execute_ShouldUseEffectiveQualifiedBaseImageTargetWhenRegistryAndPortQualifiedBaseImageAreProvided() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM scratch\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));
		_containerRegistryPreflightService.ValidatePushTarget(
				"registry.internal:5000",
				"registry.internal:5000/acme/base:1")
			.Returns(new ContainerRegistryPreflightResult(
				true,
				"https://registry.internal:5000/",
				"accepted"));

		BuildDockerImageOptions options = new() {
			Template = "base",
			BaseImage = "registry.internal:5000/acme/base:1",
			Registry = "docker.io"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0,
			"because a fully-qualified base image reference with a registry port should remain the effective push target");
		_containerRegistryPreflightService.Received(1).ValidatePushTarget(
			"registry.internal:5000",
			"registry.internal:5000/acme/base:1");
		_containerRegistryPreflightService.DidNotReceive().ValidatePushTarget(
			"docker.io",
			Arg.Any<string>());
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "tag \"registry.internal:5000/acme/base:1\" \"registry.internal:5000/acme/base:1\""));
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "push \"registry.internal:5000/acme/base:1\""));
	}

	[Test]
	[Description("Execute should fail before the expensive image build when registry preflight indicates that the configured push target is not writable.")]
	public void Execute_ShouldFailBeforeBuildWhenRegistryPreflightFails() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Registry Fail");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "image inspect \"creatio-base:8.0-v1\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});
		_containerRegistryPreflightService.ValidatePushTarget("registry.krylov.cloud",
				"registry.krylov.cloud/creatio-prod:prod_source_registry_fail")
			.Returns(new ContainerRegistryPreflightResult(
				false,
				"https://registry.krylov.cloud/",
				"Registry rejected upload initiation with 403 Forbidden."));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			Registry = "registry.krylov.cloud"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because clio should stop before the expensive image build when the registry preflight already shows that push will fail");
		_containerRegistryPreflightService.Received(1).ValidatePushTarget(
			"registry.krylov.cloud",
			"registry.krylov.cloud/creatio-prod:prod_source_registry_fail");
		GetReceivedExecutionOptions().Should().NotContain(o =>
				o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal),
			"because the image build should not start when registry preflight already failed");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Registry push preflight failed", StringComparison.Ordinal)
			&& s.Contains("403 Forbidden", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should log explicit registry tag and push messages before invoking the tag and push commands.")]
	public void Execute_ShouldLogBeforeRegistryTagAndPush() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Registry Logs");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "image inspect \"creatio-base:8.0-v1\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			Registry = "registry.krylov.cloud"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because a successful registry preflight should allow the normal tag and push flow to continue");
		_logger.Received().WriteInfo(Arg.Is<string>(s =>
			s.Contains("Tagging Docker image for registry push: registry.krylov.cloud/creatio-prod:prod_source_registry_logs", StringComparison.Ordinal)));
		_logger.Received().WriteInfo(Arg.Is<string>(s =>
			s.Contains("Pushing Docker image to registry: registry.krylov.cloud/creatio-prod:prod_source_registry_logs", StringComparison.Ordinal)));
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments == "tag \"creatio-prod:prod_source_registry_logs\" \"registry.krylov.cloud/creatio-prod:prod_source_registry_logs\"",
			"because the command should still tag the built image after logging the registry target");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments == "push \"registry.krylov.cloud/creatio-prod:prod_source_registry_logs\"",
			"because the command should still push the tagged image after logging that the registry push is starting");
	}

	[Test]
	[Description("Execute should fail bundled prod builds early when the configured base image is not available locally.")]
	public void Execute_ShouldFailBundledProdWhenBaseImageMissingLocally() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				ProcessExecutionResult result = new() {
					Started = true,
					ExitCode = 0
				};
				if (executionOptions.Arguments == "image inspect \"creatio-base:8.0-v1\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1
					});
				}

				return Task.FromResult(result);
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because bundled prod should require an explicitly available local base image");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Base image 'creatio-base:8.0-v1' is not available locally", StringComparison.Ordinal)
			&& s.Contains("clio build-docker-image --template base", StringComparison.Ordinal)));
		GetReceivedExecutionOptions().Should().NotContain(o =>
				o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal),
			"because the final image build should not start when the required base image is missing");
	}

	[Test]
	[Description("Execute should accept a bundled prod base image that exists only in the nerdctl buildkit namespace.")]
	public void Execute_ShouldUseBundledProdBaseImageFromNerdctlBuildkitNamespace() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Buildkit Only");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "--namespace k8s.io image inspect \"creatio-base:8.0-v1\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			UseNerdctl = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because nerdctl builds should accept base images that are already available to BuildKit in its own namespace");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "--namespace buildkit image inspect \"creatio-base:8.0-v1\""));
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.StartsWith("--namespace k8s.io build --pull=false", StringComparison.Ordinal),
			"because the final nerdctl build should proceed once the base image is confirmed in the buildkit namespace");
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("save --output", StringComparison.Ordinal)
			&& o.Arguments.Contains("\"creatio-base:8.0-v1\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should surface base-image inspect errors instead of reporting every inspect failure as a missing local image.")]
	public void Execute_ShouldReportInspectFailureDetails_WhenBaseImageInspectErrors() {
		// Arrange
		const string baseImage = "ghcr.io/acme/unreadable-base:1";
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Invalid Base");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == $"image inspect \"{baseImage}\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1,
						StandardError = "Error response from daemon: permission denied"
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because a base image that cannot be inspected should fail before the final Docker build starts");
		GetLoggedErrors().Should().Contain(s =>
				s.Contains($"Failed to inspect base image '{baseImage}'", StringComparison.Ordinal)
				&& s.Contains("permission denied", StringComparison.Ordinal),
			"because the container CLI's own inspect error is more useful than a generic 'not available locally'");
	}

	[Test]
	[Description("Execute should accept the bundled base template source image when it exists only in the nerdctl buildkit namespace.")]
	public void Execute_ShouldBuildBundledBaseTemplateWhenSourceImageExistsOnlyInNerdctlBuildkitNamespace() {
		// Arrange
		string templateDirectory = CreateTemplateDirectory("base-template", "FROM mcr.microsoft.com/dotnet/sdk:8.0\n");
		_templatePathProvider.ResolveTemplate("base")
			.Returns(new DockerTemplateResolution("base", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "--namespace k8s.io image inspect \"mcr.microsoft.com/dotnet/sdk:8.0\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			Template = "base",
			UseNerdctl = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because a bundled base build should proceed when the source SDK image is already available in the nerdctl buildkit namespace");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "--namespace buildkit image inspect \"mcr.microsoft.com/dotnet/sdk:8.0\""));
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.StartsWith("--namespace k8s.io build --pull=false", StringComparison.Ordinal),
			"because the nerdctl base build should continue after the source image is found in the buildkit namespace");
	}

	[Test]
	[Description("Execute should restore the cached bundled base image archive when the configured base image is not currently loaded locally.")]
	public void Execute_ShouldRestoreCachedBaseImageArchiveWhenBundledProdBaseMissing() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Cached Base");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		string cachedArchivePath = _msFileSystem.Path.Combine(_tempRoot, "settings", "docker-image-cache", "creatio-base_8.0-v1.tar");
		_msFileSystem.Directory.CreateDirectory(_msFileSystem.Path.GetDirectoryName(cachedArchivePath) ?? string.Empty);
		_msFileSystem.File.WriteAllText(cachedArchivePath, "cached-base");
		int k8sInspectCallCount = 0;
		int buildkitInspectCallCount = 0;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "--namespace k8s.io image inspect \"creatio-base:8.0-v1\"") {
					k8sInspectCallCount++;
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = k8sInspectCallCount == 1 ? 1 : 0
					});
				}

				if (executionOptions.Arguments == "--namespace buildkit image inspect \"creatio-base:8.0-v1\"") {
					buildkitInspectCallCount++;
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = buildkitInspectCallCount == 1 ? 1 : 0
					});
				}

				if (executionOptions.Arguments == "--namespace k8s.io load --input \"" + cachedArchivePath + "\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				if (executionOptions.Arguments == "--namespace buildkit load --input \"" + cachedArchivePath + "\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			UseNerdctl = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because clio should be able to restore a cached base-image archive before building bundled prod");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments == "--namespace k8s.io load --input \"" + cachedArchivePath + "\"",
			"because a missing bundled base image should be restored from the cached archive when available");
		_logger.Received().WriteInfo(Arg.Is<string>(s =>
			s.Contains("Restoring cached base image archive", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should restore the cached bundled base image archive through docker when the configured base image is missing locally.")]
	public void Execute_ShouldRestoreCachedBaseImageArchiveWhenBundledProdBaseMissingWithDocker() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Cached Base Docker");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		string cachedArchivePath = _msFileSystem.Path.Combine(_tempRoot, "settings", "docker-image-cache", "creatio-base_8.0-v1.tar");
		_msFileSystem.Directory.CreateDirectory(_msFileSystem.Path.GetDirectoryName(cachedArchivePath) ?? string.Empty);
		_msFileSystem.File.WriteAllText(cachedArchivePath, "cached-base");
		int inspectCallCount = 0;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "image inspect \"creatio-base:8.0-v1\"") {
					inspectCallCount++;
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = inspectCallCount == 1 ? 1 : 0
					});
				}

				if (executionOptions.Arguments == "load --input \"" + cachedArchivePath + "\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because docker-based hosts should also restore the cached base-image archive when the local base tag is missing");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments == "load --input \"" + cachedArchivePath + "\"",
			"because docker-based hosts should reuse the same base-image archive cache strategy");
	}

	[Test]
	[Description("Execute should pass the selected base image as a build argument for bundled docker prod builds instead of materializing a rootfs fallback.")]
	public void Execute_ShouldUseBuildArgForBundledProdWithDocker() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Docker");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		string stagedDockerfileContents = string.Empty;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "image inspect \"ghcr.io/acme/custom-base:dotnet10\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				if (executionOptions.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)) {
					stagedDockerfileContents =
						_msFileSystem.File.ReadAllText(_msFileSystem.Path.Combine(executionOptions.WorkingDirectory ?? string.Empty, "Dockerfile"));
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			BaseImage = "ghcr.io/acme/custom-base:dotnet10"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because bundled docker prod builds should keep using a normal parent-image relationship");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.Contains("--build-arg BASE_IMAGE=\"ghcr.io/acme/custom-base:dotnet10\"", StringComparison.Ordinal),
			"because docker-based bundled prod builds should receive the selected base image as a build argument");
		stagedDockerfileContents.Should().StartWith("ARG BASE_IMAGE=creatio-base:8.0-v1",
			"because docker-based bundled prod builds should keep the original Dockerfile parent-image contract");
		stagedDockerfileContents.Should().NotStartWith("FROM scratch",
			"because the rootfs materialization fallback should remain nerdctl-specific");
	}

	[Test]
	[Description("Execute should stage the cached code-server archive and pass the selected base image through a normal nerdctl build for bundled dev.")]
	public void Execute_ShouldUseSelectedBaseImageAndStageCachedCodeServerForBundledDevWithNerdctl() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Dev Source");
		string templateDirectory = CreateTemplateDirectory("dev-template",
			"ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		string cachedArchivePath = _msFileSystem.Path.Combine(_tempRoot, "cache", "code-server-4.113.1-linux-amd64.tar.gz");
		_msFileSystem.Directory.CreateDirectory(_msFileSystem.Path.GetDirectoryName(cachedArchivePath) ?? string.Empty);
		_msFileSystem.File.WriteAllText(cachedArchivePath, "cached-code-server");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", templateDirectory, true));
		_codeServerArchiveCache.EnsureArchiveAvailable("4.113.1").Returns(cachedArchivePath);
		byte[] stagedArchiveBytes = [];
		string stagedDockerfileContents = string.Empty;
		ProcessExecutionOptions buildExecutionOptions = null!;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				ProcessExecutionResult result = new() {
					Started = true,
					ExitCode = 0
				};
				if (executionOptions.Arguments == "--namespace k8s.io image inspect \"ghcr.io/acme/custom-base:dotnet10\"") {
					return Task.FromResult(result);
				}

				if (executionOptions.Arguments.StartsWith("--namespace k8s.io build --pull=false", StringComparison.Ordinal)) {
					buildExecutionOptions = executionOptions;
					stagedArchiveBytes =
						_msFileSystem.File.ReadAllBytes(_msFileSystem.Path.Combine(executionOptions.WorkingDirectory ?? string.Empty, "code-server.tar.gz"));
					stagedDockerfileContents =
						_msFileSystem.File.ReadAllText(_msFileSystem.Path.Combine(executionOptions.WorkingDirectory ?? string.Empty, "Dockerfile"));
				}

				return Task.FromResult(result);
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev",
			BaseImage = "ghcr.io/acme/custom-base:dotnet10",
			VscodeVersion = "4.113.1",
			UseNerdctl = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because bundled dev should use the selected base image and cached code-server asset when both are available");
		_processExecutor.Received().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "--namespace k8s.io image inspect \"ghcr.io/acme/custom-base:dotnet10\""));
		_codeServerArchiveCache.Received(1).EnsureArchiveAvailable("4.113.1");
		System.Text.Encoding.UTF8.GetString(stagedArchiveBytes).Should().Be("cached-code-server",
			"because bundled dev should copy the cached code-server archive into the Docker build context");
		buildExecutionOptions.Arguments.Should().Contain("--build-arg BASE_IMAGE=\"ghcr.io/acme/custom-base:dotnet10\"",
			"because bundled nerdctl builds should pass the selected base image through the Dockerfile contract");
		buildExecutionOptions.EnvironmentVariables.Should().ContainKey("CONTAINERD_NAMESPACE").
			WhoseValue.Should().Be("k8s.io",
				"because nerdctl builds should resolve BuildKit against the Kubernetes image namespace");
		stagedDockerfileContents.Should().StartWith("ARG BASE_IMAGE=creatio-base:8.0-v1",
			"because bundled nerdctl builds should keep the original Dockerfile parent-image contract");
		stagedDockerfileContents.Should().NotStartWith("FROM scratch",
			"because the rootfs materialization fallback should no longer be used for nerdctl builds");
	}

	[Test]
	[Description("Execute should build a custom template that declares ARG BASE_IMAGE without inspecting or injecting a base image when --base-image is omitted, so the template's own default applies.")]
	public void Execute_ShouldNotUseBaseImagePreflightForCustomTemplate_WhenBaseImageIsOmitted() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Custom Source");
		string templateDirectory = CreateTemplateDirectory("custom-template",
			"ARG BASE_IMAGE=ghcr.io/acme/template-default:1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate(templateDirectory)
			.Returns(new DockerTemplateResolution("custom-template", templateDirectory, false));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = templateDirectory
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because custom templates should keep their own Dockerfile contract when no base image is requested");
		GetReceivedExecutionOptions().Should().NotContain(o =>
				o.Arguments.StartsWith("image inspect", StringComparison.Ordinal),
			"because no base image was requested, so there is nothing for clio to check");
		GetReceivedExecutionOptions().Should().ContainSingle(o =>
				o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal),
			"because the custom template should still be built once");
		GetReceivedExecutionOptions().Single(o => o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal))
			.Arguments.Should().NotContain("--build-arg BASE_IMAGE=",
				"because without --base-image the custom template's own ARG BASE_IMAGE default must win");
	}

	[Test]
	[Description("Execute should pass --base-image as the BASE_IMAGE build argument, after checking the image is available locally, when a custom template directory declares ARG BASE_IMAGE.")]
	public void Execute_ShouldPassBaseImageBuildArgAfterValidation_WhenCustomTemplateDeclaresBaseImageArg() {
		// Arrange
		const string baseImage = "registry.example/creatio/product:8.3.4.1234";
		string sourceDirectory = CreateDotNetSourceDirectory("Custom Prod Source");
		string templateDirectory = CreateTemplateDirectory("custom-prod",
			"ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate(templateDirectory)
			.Returns(new DockerTemplateResolution("custom-prod", templateDirectory, false));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = templateDirectory,
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because a custom template that declares ARG BASE_IMAGE can honour an explicit --base-image");
		ProcessExecutionOptions[] executions = GetReceivedExecutionOptions();
		int inspectIndex = Array.FindIndex(executions, o => o.Arguments == $"image inspect \"{baseImage}\"");
		int buildIndex = Array.FindIndex(executions, o => o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal));
		inspectIndex.Should().BeGreaterThanOrEqualTo(0,
			"because the explicitly requested base image must be checked for local availability like it is for bundled templates");
		buildIndex.Should().BeGreaterThan(inspectIndex,
			"because the base image must be validated before the expensive image build starts");
		executions[buildIndex].Arguments.Should().Contain($"--build-arg BASE_IMAGE=\"{baseImage}\"",
			"because the Dockerfile's ARG BASE_IMAGE default must not silently win over an explicit --base-image");
	}

	[Test]
	[Description("Execute should fail a custom template build before the image build when the explicitly requested base image is not available locally.")]
	public void Execute_ShouldFailBeforeBuild_WhenCustomTemplateBaseImageIsMissingLocally() {
		// Arrange
		const string baseImage = "registry.example/creatio/product:8.3.4.1234";
		string sourceDirectory = CreateDotNetSourceDirectory("Custom Prod Missing Base");
		string templateDirectory = CreateTemplateDirectory("custom-prod",
			"ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate(templateDirectory)
			.Returns(new DockerTemplateResolution("custom-prod", templateDirectory, false));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = executionOptions.Arguments == $"image inspect \"{baseImage}\"" ? 1 : 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = templateDirectory,
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because a custom template must not be built over a base image that is not available locally");
		string missingImageError = GetLoggedErrors().Should().ContainSingle(s =>
				s.Contains($"Base image '{baseImage}' is not available locally", StringComparison.Ordinal),
			"because the user must be told which requested base image is missing").Subject;
		missingImageError.Should().NotContain("--template base",
			"because building the bundled base template cannot produce an arbitrary custom parent image");
		missingImageError.Should().Contain("Pull or tag",
			"because the fix for a custom template is to make the requested image available locally");
		GetReceivedExecutionOptions().Should().NotContain(o =>
				o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal),
			"because the image build should not start when the requested base image is missing");
	}

	[Test]
	[Description("Execute should fail fast with a clear message, before any container CLI call, when --base-image is given for a custom template whose Dockerfile does not declare ARG BASE_IMAGE.")]
	public void Execute_ShouldFailFast_WhenCustomTemplateDoesNotDeclareBaseImageArg() {
		// Arrange
		const string baseImage = "registry.example/creatio/product:8.3.4.1234";
		string sourceDirectory = CreateDotNetSourceDirectory("Custom Source Without Arg");
		string templateDirectory = CreateTemplateDirectory("custom-fixed-parent",
			"FROM mcr.microsoft.com/dotnet/aspnet:10.0\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate(templateDirectory)
			.Returns(new DockerTemplateResolution("custom-fixed-parent", templateDirectory, false));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = templateDirectory,
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because an explicit --base-image that the template cannot consume must not be silently ignored");
		GetLoggedErrors().Should().ContainSingle(s =>
				s.Contains("does not declare 'ARG BASE_IMAGE'", StringComparison.Ordinal)
				&& s.Contains($"--base-image {baseImage}", StringComparison.Ordinal)
				&& s.Contains(templateDirectory, StringComparison.Ordinal),
			"because the error must name the missing declaration, the rejected option value, and the template to fix");
		GetReceivedExecutionOptions().Should().BeEmpty(
			"because the usage error must be reported before the source is prepared or any container CLI is started");
	}

	[TestCase("ARG BASE_IMAGE\nFROM ${BASE_IMAGE}\n", true, TestName = "ARG without default")]
	[TestCase("arg BASE_IMAGE=creatio-base:8.0-v1\nfrom ${BASE_IMAGE}\n", true, TestName = "lower-case ARG and FROM keywords")]
	[TestCase("ARG BASE_IMAGE\nFROM $BASE_IMAGE\n", true, TestName = "unbraced reference")]
	[TestCase("ARG BASE_IMAGE\nFROM ${BASE_IMAGE:-creatio-base:8.0-v1}\n", true, TestName = "reference with a default")]
	[TestCase("# syntax=docker/dockerfile:1\n# FROM scratch\nARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\n", true, TestName = "commented FROM before the global ARG")]
	[TestCase("ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE} AS build\nRUN true\nFROM ${BASE_IMAGE}\n", true, TestName = "build and final stage both use the ARG")]
	[TestCase("ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE} AS base\nFROM base AS final\n", true, TestName = "final stage inherits from a stage that uses the ARG")]
	[TestCase("FROM mcr.microsoft.com/dotnet/aspnet:10.0\nARG BASE_IMAGE=creatio-base:8.0-v1\n", false, TestName = "ARG declared only after FROM")]
	[TestCase("ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM mcr.microsoft.com/dotnet/aspnet:10.0\n", false, TestName = "global ARG but fixed FROM")]
	[TestCase("ARG BASE_IMAGE=x\nARG BASE_IMAGE_TAG=10.0\nFROM mcr.microsoft.com/dotnet/aspnet:${BASE_IMAGE_TAG}\n", false, TestName = "FROM uses a similarly named ARG")]
	[TestCase("arg BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\n", true, TestName = "lower-case ARG keyword")]
	[TestCase("  ARG BASE_IMAGE=creatio-base:8.0-v1\r\nFROM ${BASE_IMAGE}\r\n", true, TestName = "indented ARG with CRLF")]
	[TestCase("ARG REGISTRY=ghcr.io BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\n", true, TestName = "ARG declaring several names")]
	[TestCase("# ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM mcr.microsoft.com/dotnet/aspnet:10.0\n", false, TestName = "commented-out ARG")]
	[TestCase("ARG BASE_IMAGE_TAG=10.0\nFROM mcr.microsoft.com/dotnet/aspnet:${BASE_IMAGE_TAG}\n", false, TestName = "different ARG with the same prefix")]
	[TestCase("ARG base_image=creatio-base:8.0-v1\nFROM ${base_image}\n", false, TestName = "ARG name in a different case")]
	[Description("Execute should honour --base-image for a custom template only when ARG BASE_IMAGE is declared in the global scope (before the first FROM) and at least one FROM references it; instructions are case-insensitive, the argument name is exact, and comments are ignored.")]
	public void Execute_ShouldDetectBaseImageArgDeclarationInCustomTemplate(string dockerfileContent, bool expectedAccepted) {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Custom Source Arg Detection");
		string templateDirectory = CreateTemplateDirectory("custom-arg-detection", dockerfileContent);
		_templatePathProvider.ResolveTemplate(templateDirectory)
			.Returns(new DockerTemplateResolution("custom-arg-detection", templateDirectory, false));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = templateDirectory,
			BaseImage = "registry.example/creatio/product:8.3.4.1234"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(expectedAccepted ? 0 : 1,
			"because --base-image is honoured only when the Dockerfile really declares ARG BASE_IMAGE");
		GetReceivedExecutionOptions().Any(o => o.Arguments.Contains("--build-arg BASE_IMAGE=", StringComparison.Ordinal))
			.Should().Be(expectedAccepted,
				"because the BASE_IMAGE build argument is passed exactly when the template can consume it");
	}

	[TestCase("   ", TestName = "whitespace only")]
	[TestCase("registry.example/creatio/product:8.3.4 --network host", TestName = "embedded space")]
	[TestCase("registry.example/creatio/product:8.3.4\t--network=host", TestName = "embedded tab")]
	[TestCase("registry.example/creatio/product:8.3.4\" --build-arg X=\"y", TestName = "double quote")]
	[TestCase("registry.example/creatio/product:8.3.4'", TestName = "single quote")]
	[TestCase("--network=host", TestName = "leading double dash")]
	[TestCase("-registry.example/creatio/product:8.3.4", TestName = "leading dash")]
	[TestCase("registry.example/creatio/product:8.3.4\u0007", TestName = "control character")]
	[TestCase("registry.example/creatio/\nproduct:8.3.4", TestName = "newline")]
	[TestCase("registry.example/Creatio/Product:8.3.4", TestName = "upper-case repository path")]
	[TestCase("registry.example/creatio/product:", TestName = "empty tag")]
	[Description("Execute should reject a --base-image value that is not a Docker image reference before any container CLI call, so the value can never inject options into the docker command line.")]
	public void Execute_ShouldRejectBaseImageThatIsNotAnImageReference(string baseImage) {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Invalid Base Reference");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
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
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because a value that is not an image reference must be refused");
		GetLoggedErrors().Should().ContainSingle(s =>
				s.Contains("--base-image", StringComparison.Ordinal)
				&& s.Contains("is not a valid Docker image reference", StringComparison.Ordinal),
			"because the user must be told that the --base-image value itself is malformed");
		GetReceivedExecutionOptions().Should().BeEmpty(
			"because a malformed value must be refused before it can reach any container CLI command line");
	}

	[TestCase("creatio-base:8.0-v1", TestName = "short name with tag")]
	[TestCase("registry.internal:5000/acme/base:1", TestName = "registry with port")]
	[TestCase("localhost:5000/creatio/product:8.3.4.1234", TestName = "localhost registry")]
	[TestCase("Registry.Example.COM/creatio/product_net10__x:8.3.4-rc_1", TestName = "upper-case registry host and separators")]
	[TestCase("registry.example/creatio/product@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", TestName = "digest")]
	[TestCase("registry.example/creatio/product:8.3.4@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", TestName = "tag and digest")]
	[TestCase("[::1]:5000/creatio/product:8.3.4", TestName = "IPv6 registry")]
	[TestCase("  ghcr.io/acme/creatio-base:dotnet10  ", TestName = "surrounding whitespace is trimmed")]
	[Description("Execute should accept every valid Docker image reference shape for --base-image and pass it through as the BASE_IMAGE build argument.")]
	public void Execute_ShouldAcceptValidBaseImageReference(string baseImage) {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Valid Base Reference");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
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
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because a well-formed image reference must not be refused by the validation");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)
				&& o.Arguments.Contains($"--build-arg BASE_IMAGE=\"{baseImage.Trim()}\"", StringComparison.Ordinal),
			"because the validated reference must still reach the build unchanged apart from trimming");
	}

	[Test]
	[Description("Execute should keep passing the default base image as the BASE_IMAGE build argument for bundled prod when --base-image is omitted.")]
	public void Execute_ShouldPassDefaultBaseImageBuildArgForBundledProd_WhenBaseImageIsOmitted() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Default Base");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because bundled prod builds should still work with the default local base image");
		GetReceivedExecutionOptions().Should().Contain(o => o.Arguments == "image inspect \"creatio-base:8.0-v1\"",
			"because bundled prod should still validate the default base image before building");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)
				&& o.Arguments.Contains("--build-arg BASE_IMAGE=\"creatio-base:8.0-v1\"", StringComparison.Ordinal),
			"because bundled prod should keep receiving the default base image as a build argument");
	}

	[Test]
	[Description("Execute should apply --base-image to bundled prod and leave the bundled db template of the same batch untouched instead of rejecting it for lacking ARG BASE_IMAGE.")]
	public void Execute_ShouldNotRejectBundledDbTemplate_WhenBatchPassesBaseImage() {
		// Arrange
		const string baseImage = "registry.example/creatio/product:8.3.4.1234";
		string sourceDirectory = CreateDotNetSourceDirectoryWithDatabase("Batch Base Image");
		string dbTemplateDirectory = CreateTemplateDirectory("db-template", "FROM busybox:1.36.1\nCOPY db/ /db/\n");
		string prodTemplateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("db")
			.Returns(new DockerTemplateResolution("db", dbTemplateDirectory, true));
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", prodTemplateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "db,prod",
			BaseImage = baseImage
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the ARG BASE_IMAGE requirement applies to custom templates only, not to the bundled db template");
		ProcessExecutionOptions[] builds = GetReceivedExecutionOptions()
			.Where(o => o.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal))
			.ToArray();
		builds.Should().HaveCount(2, "because both templates of the batch should be built");
		builds.Count(o => o.Arguments.Contains($"--build-arg BASE_IMAGE=\"{baseImage}\"", StringComparison.Ordinal))
			.Should().Be(1, "because only bundled prod consumes the requested base image");
	}

	[Test]
	[Description("Execute should strip the source db folder from a bundled prod application image when include-db is not requested.")]
	public void Execute_ShouldStripDatabaseFolderFromApplicationImage_WhenIncludeDbIsNotRequested() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectoryWithDatabase("app-with-db");
		string templateDirectory = CreateTemplateDirectory("prod-template", "FROM base\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		bool stagedDatabaseExists = true;
		string dockerIgnoreContents = string.Empty;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)) {
					string workingDirectory = executionOptions.WorkingDirectory ?? string.Empty;
					stagedDatabaseExists = _msFileSystem.Directory.Exists(
						_msFileSystem.Path.Combine(workingDirectory, "source", "db"));
					string dockerIgnorePath = _msFileSystem.Path.Combine(workingDirectory, ".dockerignore");
					dockerIgnoreContents = _msFileSystem.File.Exists(dockerIgnorePath)
						? _msFileSystem.File.ReadAllText(dockerIgnorePath)
						: string.Empty;
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because a source containing a db folder is still a valid application source");
		stagedDatabaseExists.Should().BeFalse(
			"because the default behaviour keeps the database payload out of the application image");
		dockerIgnoreContents.Should().Contain("source/db",
			"because clio also excludes the database payload through .dockerignore by default");
	}

	[Test]
	[Description("Execute should keep the source db folder in a bundled prod application image when include-db is requested.")]
	public void Execute_ShouldKeepDatabaseFolderInApplicationImage_WhenIncludeDbIsRequested() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectoryWithDatabase("app-with-db-included");
		string templateDirectory = CreateTemplateDirectory("prod-template-include", "FROM base\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		string stagedBackupContents = string.Empty;
		string dockerIgnoreContents = string.Empty;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)) {
					string workingDirectory = executionOptions.WorkingDirectory ?? string.Empty;
					string stagedBackupPath = _msFileSystem.Path.Combine(
						workingDirectory, "source", "db", "backup.dump");
					stagedBackupContents = _msFileSystem.File.Exists(stagedBackupPath)
						? _msFileSystem.File.ReadAllText(stagedBackupPath)
						: string.Empty;
					string dockerIgnorePath = _msFileSystem.Path.Combine(workingDirectory, ".dockerignore");
					dockerIgnoreContents = _msFileSystem.File.Exists(dockerIgnorePath)
						? _msFileSystem.File.ReadAllText(dockerIgnorePath)
						: string.Empty;
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod",
			IncludeDb = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because include-db does not change how the application source is validated");
		stagedBackupContents.Should().Be("backup-data",
			"because include-db must stage the source db payload so the built image carries it at /app/db");
		dockerIgnoreContents.Should().NotContain("source/db",
			"because a .dockerignore entry for the database payload would defeat include-db");
	}

	[Test]
	[Description("Execute should ignore include-db for the bundled db template, which always packages only the database payload.")]
	public void Execute_ShouldIgnoreIncludeDbForBundledDbTemplate() {
		// Arrange
		string sourceDirectory = CreateDatabaseSourceDirectory("Db Only Source");
		string templateDirectory = CreateTemplateDirectory("db-template-include", "FROM busybox:1.36.1\nCOPY db/ ./\n");
		_templatePathProvider.ResolveTemplate("db")
			.Returns(new DockerTemplateResolution("db", templateDirectory, true));
		string stagedBackupContents = string.Empty;
		bool stagedApplicationSourceExists = true;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)) {
					string workingDirectory = executionOptions.WorkingDirectory ?? string.Empty;
					stagedBackupContents = _msFileSystem.File.ReadAllText(
						_msFileSystem.Path.Combine(workingDirectory, "db", "backup.dump"));
					stagedApplicationSourceExists = _msFileSystem.Directory.Exists(
						_msFileSystem.Path.Combine(workingDirectory, "source"));
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "db",
			IncludeDb = true
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because include-db is irrelevant to the bundled db template and must not alter its build");
		stagedBackupContents.Should().Be("backup-data",
			"because the bundled db template stages the database payload regardless of include-db");
		stagedApplicationSourceExists.Should().BeFalse(
			"because include-db must not make the bundled db template stage application source files");
	}

	[Test]
	[Description("Execute should build the bundled db template from a directory source by staging only the db folder and applying db source labels.")]
	public void Execute_ShouldBuildBundledDbTemplateFromDirectorySource() {
		// Arrange
		string sourceDirectory = CreateDatabaseSourceDirectory("Db Backup Source");
		string templateDirectory = CreateTemplateDirectory("db-template", "FROM busybox:1.36.1\nCOPY db/ ./\n");
		_templatePathProvider.ResolveTemplate("db")
			.Returns(new DockerTemplateResolution("db", templateDirectory, true));
		string stagedBackupContents = string.Empty;
		bool stagedApplicationSourceExists = false;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)) {
					string workingDirectory = executionOptions.WorkingDirectory ?? string.Empty;
					stagedBackupContents = _msFileSystem.File.ReadAllText(
						_msFileSystem.Path.Combine(workingDirectory, "db", "backup.dump"));
					stagedApplicationSourceExists = _msFileSystem.Directory.Exists(
						_msFileSystem.Path.Combine(workingDirectory, "source"));
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "db"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the bundled db template should accept a source directory that only contains a db backup");
		stagedBackupContents.Should().Be("backup-data",
			"because the bundled db template should copy the source db folder into the Docker build context");
		stagedApplicationSourceExists.Should().BeFalse(
			"because the bundled db template should not stage the regular application source directory");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.Contains("-t \"creatio-db:db_backup_source\"", StringComparison.Ordinal)
				&& o.Arguments.Contains("--label \"org.creatio.capability.db-source=Db Backup Source\"", StringComparison.Ordinal),
			"because bundled db images should be tagged as creatio-db and labeled with the original source leaf name");
	}

	[Test]
	[Description("Execute should build the bundled db template from a zip source by extracting and staging the db folder from the archive root.")]
	public void Execute_ShouldBuildBundledDbTemplateFromZipSource() {
		// Arrange
		string sourceZipPath = _msFileSystem.Path.Combine(_tempRoot, "8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU.zip");
		_msFileSystem.File.WriteAllText(sourceZipPath, "zip-placeholder");
		string templateDirectory = CreateTemplateDirectory("db-template", "FROM busybox:1.36.1\nCOPY db/ ./\n");
		_templatePathProvider.ResolveTemplate("db")
			.Returns(new DockerTemplateResolution("db", templateDirectory, true));
		_zipFile.When(z => z.ExtractToDirectory(sourceZipPath, Arg.Any<string>()))
			.Do(callInfo => {
				string extractedPath = callInfo.ArgAt<string>(1);
				string extractedDbPath = _msFileSystem.Path.Combine(extractedPath, "db");
				_msFileSystem.Directory.CreateDirectory(extractedDbPath);
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedDbPath, "backup.dump"), "zip-backup-data");
			});
		string stagedBackupContents = string.Empty;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("build --pull=false", StringComparison.Ordinal)) {
					stagedBackupContents = _msFileSystem.File.ReadAllText(
						_msFileSystem.Path.Combine(executionOptions.WorkingDirectory ?? string.Empty, "db", "backup.dump"));
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});

		BuildDockerImageOptions options = new() {
			SourcePath = sourceZipPath,
			Template = "db"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the bundled db template should accept zip sources that expose a db folder");
		stagedBackupContents.Should().Be("zip-backup-data",
			"because the extracted db folder should be the only payload staged into the image build context");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.Contains("--label \"org.creatio.capability.db-source=8.3.4.1971_StudioNet8_Softkey_PostgreSQL_ENU\"", StringComparison.Ordinal),
			"because bundled db images should label the backup source using the zip file name without the extension");
	}

	[Test]
	[Description("Execute should fail the bundled db template before invoking Docker when the source does not contain a db folder.")]
	public void Execute_ShouldRejectBundledDbTemplateWhenDatabaseDirectoryIsMissing() {
		// Arrange
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, "missing-db");
		_msFileSystem.Directory.CreateDirectory(sourceDirectory);
		string templateDirectory = CreateTemplateDirectory("db-template", "FROM busybox:1.36.1\nCOPY db/ ./\n");
		_templatePathProvider.ResolveTemplate("db")
			.Returns(new DockerTemplateResolution("db", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "db"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because the bundled db template cannot build without a db directory to package");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("does not contain a 'db' directory", StringComparison.Ordinal)));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.Contains("build --pull=false", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should reject .NET Framework payloads before trying to invoke Docker.")]
	public void Execute_ShouldRejectNetFrameworkSource() {
		// Arrange
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, "framework-app");
		_msFileSystem.Directory.CreateDirectory(sourceDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Web.config"), "<configuration />");
		string templateDirectory = CreateTemplateDirectory("prod-template", "FROM scratch\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, false));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

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
	[Description("Execute should reject duplicate template names in a comma-separated batch request.")]
	public void Execute_ShouldRejectDuplicateTemplateNames() {
		// Arrange
		BuildDockerImageOptions options = new() {
			SourcePath = "/workspace/app",
			Template = "dev,prod,dev"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because the command should fail fast when the same template is requested more than once");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("duplicates", StringComparison.Ordinal)
			&& s.Contains("dev", StringComparison.Ordinal)));
		_templatePathProvider.DidNotReceive().ResolveTemplate(Arg.Any<string>());
	}

	[Test]
	[Description("Execute should extract the zip source and probe the container CLI only once when multiple templates are built from one batch request.")]
	public void Execute_ShouldReuseZipExtractionAndCliDetectionAcrossTemplateBatch() {
		// Arrange
		string sourceZipPath = _msFileSystem.Path.Combine(_tempRoot, "Multi Source.zip");
		_msFileSystem.File.WriteAllText(sourceZipPath, "zip-placeholder");
		string devTemplateDirectory = CreateTemplateDirectory("dev-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		string prodTemplateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", devTemplateDirectory, true));
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", prodTemplateDirectory, true));
		_zipFile.When(z => z.ExtractToDirectory(sourceZipPath, Arg.Any<string>()))
			.Do(callInfo => {
				string extractedPath = callInfo.ArgAt<string>(1);
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedPath, "Terrasoft.WebHost.dll"), "dll");
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedPath, "Terrasoft.WebHost.dll.config"), "config");
				_msFileSystem.Directory.CreateDirectory(_msFileSystem.Path.Combine(extractedPath, "db"));
				_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(extractedPath, "db", "backup.dump"), "backup");
			});
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("image inspect", StringComparison.Ordinal)) {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});
		BuildDockerImageOptions options = new() {
			SourcePath = sourceZipPath,
			Template = "dev,prod"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because both bundled templates should be able to reuse the same prepared source payload");
		_zipFile.Received(1).ExtractToDirectory(sourceZipPath, Arg.Any<string>());
		_processExecutor.Received(1).ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker" && o.Arguments == "info"));
		_processExecutor.Received(1).ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "docker" && o.Arguments == "--version"));
		GetReceivedExecutionOptions().Count(o => o.Arguments.Contains("build --pull=false", StringComparison.Ordinal))
			.Should().Be(2, "because the batch should still execute one docker build per requested template");
	}

	[Test]
	[Description("Execute should continue building later templates when an earlier template build fails in the batch.")]
	public void Execute_ShouldContinueBatchAfterTemplateBuildFailure() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Batch Failure Source");
		string devTemplateDirectory = CreateTemplateDirectory("dev-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		string prodTemplateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", prodTemplateDirectory, true));
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", devTemplateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("image inspect", StringComparison.Ordinal)
					|| executionOptions.Arguments == "info"
					|| executionOptions.Arguments == "--version") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				if (executionOptions.Arguments.Contains("-t \"creatio-prod:batch_failure_source\"", StringComparison.Ordinal)) {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1,
						StandardError = "prod-build-failed"
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});
		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "prod,dev"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because the overall batch should report failure when any template build fails");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.Contains("-t \"creatio-dev:batch_failure_source\"", StringComparison.Ordinal),
			"because later templates should still be built after an earlier build failure in the batch");
		_logger.Received().WriteInfo(Arg.Is<string>(s =>
			s.Contains("Docker image batch summary", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Execute should save one tar file per template when a multi-template batch uses an output directory.")]
	public void Execute_ShouldSaveOneTarPerTemplateWhenBatchUsesOutputDirectory() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Batch Output Source");
		string devTemplateDirectory = CreateTemplateDirectory("dev-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		string prodTemplateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		string outputDirectory = _msFileSystem.Path.Combine(_tempRoot, "exports");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", devTemplateDirectory, true));
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", prodTemplateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments.StartsWith("image inspect", StringComparison.Ordinal)
					|| executionOptions.Arguments == "info"
					|| executionOptions.Arguments == "--version") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 0
					});
				}

				return Task.FromResult(new ProcessExecutionResult {
					Started = true,
					ExitCode = 0
				});
			});
		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev,prod",
			OutputPath = outputDirectory
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because the batch should treat the output path as a directory and save one tar per template");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.Contains("save --output", StringComparison.Ordinal)
				&& o.Arguments.Contains("exports", StringComparison.Ordinal)
				&& o.Arguments.Contains("creatio-dev_batch_output_source.tar", StringComparison.Ordinal),
			"because the dev template should save into its own deterministic tar path under the output directory");
		GetReceivedExecutionOptions().Should().Contain(o =>
				o.Arguments.Contains("save --output", StringComparison.Ordinal)
				&& o.Arguments.Contains("exports", StringComparison.Ordinal)
				&& o.Arguments.Contains("creatio-prod_batch_output_source.tar", StringComparison.Ordinal),
			"because the prod template should save into its own deterministic tar path under the output directory");
	}

	[Test]
	[Description("Execute should reject a file-like output path when multiple templates are requested.")]
	public void Execute_ShouldRejectFileLikeOutputPathForTemplateBatch() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Batch Output Validation");
		string devTemplateDirectory = CreateTemplateDirectory("dev-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		string prodTemplateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("dev")
			.Returns(new DockerTemplateResolution("dev", devTemplateDirectory, true));
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", prodTemplateDirectory, true));
		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = "dev,prod",
			OutputPath = _msFileSystem.Path.Combine(_tempRoot, "exports.tar")
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because multi-template output paths must point to a directory instead of a single tar file");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("must be a directory", StringComparison.Ordinal)));
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>());
	}

	private string CreateDotNetSourceDirectoryWithDatabase(string leafName) {
		string sourceDirectory = CreateDotNetSourceDirectory(leafName);
		string databaseDirectory = _msFileSystem.Path.Combine(sourceDirectory, "db");
		_msFileSystem.Directory.CreateDirectory(databaseDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(databaseDirectory, "backup.dump"), "backup-data");
		return sourceDirectory;
	}

	private string CreateDotNetSourceDirectory(string leafName) {
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		_msFileSystem.Directory.CreateDirectory(sourceDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Terrasoft.WebHost.dll"), "dll");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Terrasoft.WebHost.dll.config"), "config");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "appsettings.json"), "{}");
		return sourceDirectory;
	}

	private string CreateDatabaseSourceDirectory(string leafName) {
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		string databaseDirectory = _msFileSystem.Path.Combine(sourceDirectory, "db");
		_msFileSystem.Directory.CreateDirectory(databaseDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(databaseDirectory, "backup.dump"), "backup-data");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "ignore-me.txt"), "not-part-of-db-image");
		return sourceDirectory;
	}

	private string CreateTemplateDirectory(string leafName, string dockerfileContent) {
		string templateDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		_msFileSystem.Directory.CreateDirectory(templateDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(templateDirectory, "Dockerfile"), dockerfileContent);
		return templateDirectory;
	}

	private string[] GetLoggedErrors() {
		return _logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Select(call => call.GetArguments())
			.Where(arguments => arguments.Length == 1 && arguments[0] is string)
			.Select(arguments => (string)arguments[0])
			.ToArray();
	}

	private ProcessExecutionOptions[] GetReceivedExecutionOptions() {
		return _processExecutor.ReceivedCalls()
			.Select(call => call.GetArguments())
			.Where(arguments => arguments.Length == 1 && arguments[0] is ProcessExecutionOptions)
			.Select(arguments => (ProcessExecutionOptions)arguments[0])
			.ToArray();
	}
}
