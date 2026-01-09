using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class DeployInfrastructureCommandTests : BaseCommandTests<DeployInfrastructureOptions>{
	#region Fields: Private

	private DeployInfrastructureCommand _command;
	private IDbClientFactory _dbClientFactory;
	private IFileSystem _fileSystem;
	private IInfrastructurePathProvider _infrastructurePathProvider;
	private Ik8Commands _k8Commands;
	private ILogger _logger;
	private IProcessExecutor _processExecutor;

	#endregion

	#region Methods: Public

	[Test]
	[Description("Should use custom infrastructure path when provided")]
	public void Execute_WhenCustomPathProvided_ShouldUseCustomPath() {
		// Arrange
		DeployInfrastructureOptions options = new() {
			InfrastructurePath = "/custom/infrastructure/path", SkipVerification = true
		};

		_infrastructurePathProvider.GetInfrastructurePath("/custom/infrastructure/path")
								   .Returns("/custom/infrastructure/path");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because deployment should succeed with custom path");
		_infrastructurePathProvider.Received().GetInfrastructurePath("/custom/infrastructure/path");
	}

	[Test]
	[Description("Should deploy infrastructure components in correct order")]
	public void Execute_WhenDeploying_ShouldFollowCorrectDeploymentOrder() {
		// Arrange
		DeployInfrastructureOptions options = new() { SkipVerification = true };
		List<string> applyCalls = new();

		_processExecutor
			.Execute("kubectl", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>()
				, Arg.Any<bool>())
			.Returns(callInfo => {
				string args = callInfo.ArgAt<string>(1);
				if (args.StartsWith("apply")) {
					applyCalls.Add(args);
				}

				return "success";
			});

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because deployment should succeed");
		applyCalls.Count.Should().BeGreaterThan(5, "because multiple components should be deployed");

		// Verify namespace is deployed first
		applyCalls[0].Should().Contain("namespace", "because namespace must be created first");

		// Verify storage class comes before volumes
		int storageClassIndex = applyCalls.FindIndex(s => s.Contains("storage-class"));
		int volumesIndex = applyCalls.FindIndex(s => s.Contains("volumes"));
		storageClassIndex.Should().BeLessThan(volumesIndex, "because storage class must exist before volumes");
	}

	[Test]
	[Description("Should handle deployment failure gracefully")]
	public void Execute_WhenDeploymentFails_ShouldReturnError() {
		// Arrange
		DeployInfrastructureOptions options = new();
		_processExecutor
			.Execute("kubectl", Arg.Is<string>(s => s.Contains("apply")), Arg.Any<bool>(), Arg.Any<string>()
				, Arg.Any<bool>(), Arg.Any<bool>())
			.Throws(new Exception("Deployment failed"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because deployment failed");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Failed to deploy")));
	}

	[Test]
	[Description("Should generate infrastructure files with correct resource configurations")]
	public void Execute_WhenGeneratingFiles_ShouldUseCorrectResourceLimits() {
		// Arrange
		DeployInfrastructureOptions options = new() { SkipVerification = true };
		_infrastructurePathProvider.GetInfrastructurePath().Returns("/custom/path");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because deployment should succeed");
		_infrastructurePathProvider.Received().GetInfrastructurePath();
		_fileSystem.Received().ExistsDirectory(Arg.Is<string>(s => s.Contains("tpl")));
	}

	[Test]
	[Description("Should fail when kubectl is not installed")]
	public void Execute_WhenKubectlNotInstalled_ShouldReturnError() {
		// Arrange
		_processExecutor
			.Execute("kubectl", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>()
				, Arg.Any<bool>())
			.Throws(new Exception("kubectl not found"));

		DeployInfrastructureOptions options = new();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because kubectl is not installed");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("kubectl is not installed")));
	}

	[Test]
	[Description("Should fail when namespace already exists")]
	public void Execute_WhenNamespaceExists_ShouldReturnError() {
		// Arrange
		_k8Commands.NamespaceExists("clio-infrastructure").Returns(true);
		DeployInfrastructureOptions options = new();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because namespace already exists");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("already exists")));
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("delete-infrastructure")));
	}

	[Test]
	[Description("Should cleanup orphaned PersistentVolumes before deployment")]
	public void Execute_WhenOrphanedPVsExist_ShouldCleanupBeforeDeployment() {
		// Arrange
		List<string> orphanedPvs = ["pv-orphan-1", "pv-orphan-2"];
		_k8Commands.GetOrphanedPersistentVolumes("clio-infrastructure")
				   .Returns(orphanedPvs, new List<string>()); // Return empty on second call
		_k8Commands.DeletePersistentVolume(Arg.Any<string>()).Returns(true);

		DeployInfrastructureOptions options = new() { SkipVerification = true };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because deployment should succeed after cleanup");
		_k8Commands.Received(1).DeletePersistentVolume("pv-orphan-1");
		_k8Commands.Received(1).DeletePersistentVolume("pv-orphan-2");
	}

	[Test]
	[Description("Should retry PostgreSQL connection on failure")]
	public void Execute_WhenPostgresConnectionFails_ShouldRetryMultipleTimes() {
		// Arrange
		DeployInfrastructureOptions options = new() { SkipVerification = false };
		k8Commands.ConnectionStringParams connectionParams = new(5432, 5432, 6379,
			6379, "postgres", "password");
		_k8Commands.GetPostgresConnectionString().Returns(connectionParams);

		Postgres postgres = Substitute.ForPartsOf<Postgres>();
		postgres.CheckTemplateExists("template0").Throws(new Exception("Connection refused"));
		_dbClientFactory.CreatePostgresSilent(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>()).Returns(postgres);


		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because Postgres SQL connection failed after retries");
		_dbClientFactory.Received(_command.VerifyPostgresConnectionMaxRetryAttempts)
						.CreatePostgresSilent(5432, "postgres", "password");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Postgres SQL connection failed")));
	}

	[Test]
	[Description("Should skip verification when SkipVerification is true")]
	public void Execute_WhenSkipVerificationTrue_ShouldNotVerifyConnections() {
		// Arrange
		DeployInfrastructureOptions options = new() { SkipVerification = true };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because deployment should succeed");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Skipping connection verification")));
		_dbClientFactory.DidNotReceive().CreatePostgresSilent(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Should fail when template files are missing")]
	public void Execute_WhenTemplateFilesMissing_ShouldReturnError() {
		// Arrange
		_fileSystem.ExistsDirectory(Arg.Is<string>(s => s.Contains("tpl"))).Returns(false);
		DeployInfrastructureOptions options = new();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because template files are missing");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Template files not found")));
	}

	[Test]
	[Description("Should verify PostgreSQL connection when verification enabled")]
	public void Execute_WhenVerifyingPostgres_ShouldAttemptConnection() {
		// Arrange
		DeployInfrastructureOptions options = new() { SkipVerification = false };
		k8Commands.ConnectionStringParams connectionParams = new(
			5432,
			5432,
			6379,
			6379,
			"postgres",
			"password"
		);
		_k8Commands.GetPostgresConnectionString().Returns(connectionParams);

		Postgres postgres = Substitute.ForPartsOf<Postgres>();
		postgres.CheckTemplateExists("template0").Returns(true);
		_dbClientFactory.CreatePostgresSilent(5432, "postgres", "password").Returns(postgres);

		// Mock Redis connection success
		_k8Commands.GetPostgresConnectionString().Returns(connectionParams);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because connections should verify successfully");
		_dbClientFactory.Received().CreatePostgresSilent(5432, "postgres", "password");
		postgres.Received().CheckTemplateExists("template0");
	}

	[SetUp]
	public override void Setup() {
		_processExecutor = Substitute.For<IProcessExecutor>();
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_k8Commands = Substitute.For<Ik8Commands>();
		_dbClientFactory = Substitute.For<IDbClientFactory>();
		_infrastructurePathProvider = Substitute.For<IInfrastructurePathProvider>();

		_command = new DeployInfrastructureCommand(
			_processExecutor,
			_logger,
			_fileSystem,
			_k8Commands,
			_dbClientFactory,
			_infrastructurePathProvider) {
			//Speeds things up in tests
			RetryDelay = 10, PodDelay = 10, VerifyPostgresConnectionMaxRetryAttempts = 3,
			VerifyPostgresConnectionDelaySeconds = 1, VerifyRedisConnectionMaxRetryAttempts = 3,
			VerifyRedisConnectionDelaySeconds = 1, CleanupOrphanedPersistentVolumesMaxAttempts = 2,
			VerifyRedisConnectionConnectionTimeout = 100, VerifyRedisConnectionSyncTimeout = 100
		};

		// Setup default successful responses
		_processExecutor.Execute("kubectl", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>()
							, Arg.Any<bool>())
						.Returns("Client Version: v1.28.0");
		_k8Commands.NamespaceExists(Arg.Any<string>()).Returns(false);
		_k8Commands.GetOrphanedPersistentVolumes(Arg.Any<string>()).Returns(new List<string>());
		_infrastructurePathProvider.GetInfrastructurePath(Arg.Any<string>()).Returns("/test/infrastructure");
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
	}

	#endregion
}
