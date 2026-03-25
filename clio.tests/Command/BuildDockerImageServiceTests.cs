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
	[Description("Execute should surface base-image inspect errors instead of reporting every inspect failure as a missing local image.")]
	public void Execute_ShouldReportInspectFailureDetailsForInvalidBaseImageReference() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Prod Source Invalid Base");
		string templateDirectory = CreateTemplateDirectory("prod-template", "ARG BASE_IMAGE=creatio-base:8.0-v1\nFROM ${BASE_IMAGE}\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate("prod")
			.Returns(new DockerTemplateResolution("prod", templateDirectory, true));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "image inspect \"INVALID@@REF\"") {
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = 1,
						StandardError = "Error: invalid reference format"
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
			BaseImage = "INVALID@@REF"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(1, "because invalid base image references should fail before the final Docker build starts");
		_logger.Received().WriteError(Arg.Is<string>(s =>
			s.Contains("Failed to inspect base image 'INVALID@@REF'", StringComparison.Ordinal)
			&& s.Contains("invalid reference format", StringComparison.Ordinal)));
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
		int inspectCallCount = 0;
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(callInfo => {
				ProcessExecutionOptions executionOptions = callInfo.Arg<ProcessExecutionOptions>();
				if (executionOptions.Arguments == "--namespace k8s.io image inspect \"creatio-base:8.0-v1\"") {
					inspectCallCount++;
					return Task.FromResult(new ProcessExecutionResult {
						Started = true,
						ExitCode = inspectCallCount == 1 ? 1 : 0
					});
				}

				if (executionOptions.Arguments == "--namespace k8s.io load --input \"" + cachedArchivePath + "\"") {
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
	[Description("Execute should build a custom template without trying to inspect or inject a base image reference.")]
	public void Execute_ShouldNotUseBaseImagePreflightForCustomTemplate() {
		// Arrange
		string sourceDirectory = CreateDotNetSourceDirectory("Custom Source");
		string templateDirectory = CreateTemplateDirectory("custom-template", "FROM scratch\nCOPY source/ ./\n");
		_templatePathProvider.ResolveTemplate(templateDirectory)
			.Returns(new DockerTemplateResolution("custom-template", templateDirectory, false));
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));

		BuildDockerImageOptions options = new() {
			SourcePath = sourceDirectory,
			Template = templateDirectory,
			BaseImage = "ghcr.io/acme/unused-base:tag"
		};

		// Act
		int result = _service.Execute(options);

		// Assert
		result.Should().Be(0, "because custom templates should keep their own Dockerfile contract without bundled base-image injection");
		_processExecutor.DidNotReceive().ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments.StartsWith("image inspect", StringComparison.Ordinal)));
		GetReceivedExecutionOptions().Should().Contain(o =>
				!o.Arguments.Contains($"--build-arg BASE_IMAGE=", StringComparison.Ordinal),
			"because custom templates should not receive the bundled base-image build argument");
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

	private string CreateDotNetSourceDirectory(string leafName) {
		string sourceDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		_msFileSystem.Directory.CreateDirectory(sourceDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Terrasoft.WebHost.dll"), "dll");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "Terrasoft.WebHost.dll.config"), "config");
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(sourceDirectory, "appsettings.json"), "{}");
		return sourceDirectory;
	}

	private string CreateTemplateDirectory(string leafName, string dockerfileContent) {
		string templateDirectory = _msFileSystem.Path.Combine(_tempRoot, leafName);
		_msFileSystem.Directory.CreateDirectory(templateDirectory);
		_msFileSystem.File.WriteAllText(_msFileSystem.Path.Combine(templateDirectory, "Dockerfile"), dockerfileContent);
		return templateDirectory;
	}

	private ProcessExecutionOptions[] GetReceivedExecutionOptions() {
		return _processExecutor.ReceivedCalls()
			.Select(call => call.GetArguments())
			.Where(arguments => arguments.Length == 1 && arguments[0] is ProcessExecutionOptions)
			.Select(arguments => (ProcessExecutionOptions)arguments[0])
			.ToArray();
	}
}
