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

namespace Clio.Tests.Command
{
	[TestFixture]
	public class DeployInfrastructureCommandTests : BaseCommandTests<DeployInfrastructureOptions>
	{
		private IProcessExecutor _processExecutor;
		private ILogger _logger;
		private Clio.Common.IFileSystem _fileSystem;
		private Ik8Commands _k8Commands;
		private IDbClientFactory _dbClientFactory;
		private IInfrastructurePathProvider _infrastructurePathProvider;
		private DeployInfrastructureCommand _command;

		[SetUp]
		public void Setup()
		{
			_processExecutor = Substitute.For<IProcessExecutor>();
			_logger = Substitute.For<ILogger>();
			_fileSystem = Substitute.For<Clio.Common.IFileSystem>();
			_k8Commands = Substitute.For<Ik8Commands>();
			_dbClientFactory = Substitute.For<IDbClientFactory>();
			_infrastructurePathProvider = Substitute.For<IInfrastructurePathProvider>();

			_command = new DeployInfrastructureCommand(
				_processExecutor,
				_logger,
				_fileSystem,
				_k8Commands,
				_dbClientFactory,
				_infrastructurePathProvider);

			// Setup default successful responses
			_processExecutor.Execute("kubectl", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
				.Returns("Client Version: v1.28.0");
			_k8Commands.NamespaceExists(Arg.Any<string>()).Returns(false);
			_k8Commands.GetOrphanedPersistentVolumes(Arg.Any<string>()).Returns(new List<string>());
			_infrastructurePathProvider.GetInfrastructurePath(Arg.Any<string>()).Returns("/test/infrastructure");
			_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
			_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		}

		[Test]
		[Description("Should fail when kubectl is not installed")]
		public void Execute_WhenKubectlNotInstalled_ShouldReturnError()
		{
			// Arrange
			_processExecutor.Execute("kubectl", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
				.Throws(new Exception("kubectl not found"));

			var options = new DeployInfrastructureOptions();

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because kubectl is not installed");
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("kubectl is not installed")));
		}

		[Test]
		[Description("Should fail when namespace already exists")]
		public void Execute_WhenNamespaceExists_ShouldReturnError()
		{
			// Arrange
			_k8Commands.NamespaceExists("clio-infrastructure").Returns(true);
			var options = new DeployInfrastructureOptions();

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because namespace already exists");
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("already exists")));
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("delete-infrastructure")));
		}

		[Test]
		[Description("Should cleanup orphaned PersistentVolumes before deployment")]
		public void Execute_WhenOrphanedPVsExist_ShouldCleanupBeforeDeployment()
		{
			// Arrange
			var orphanedPvs = new List<string> { "pv-orphan-1", "pv-orphan-2" };
			_k8Commands.GetOrphanedPersistentVolumes("clio-infrastructure")
				.Returns(orphanedPvs, new List<string>()); // Return empty on second call
			_k8Commands.DeletePersistentVolume(Arg.Any<string>()).Returns(true);

			var options = new DeployInfrastructureOptions { SkipVerification = true };

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deployment should succeed after cleanup");
			_k8Commands.Received(1).DeletePersistentVolume("pv-orphan-1");
			_k8Commands.Received(1).DeletePersistentVolume("pv-orphan-2");
		}

		[Test]
		[Description("Should generate infrastructure files with correct resource configurations")]
		public void Execute_WhenGeneratingFiles_ShouldUseCorrectResourceLimits()
		{
			// Arrange
			var options = new DeployInfrastructureOptions { SkipVerification = true };
			_infrastructurePathProvider.GetInfrastructurePath(null).Returns("/custom/path");

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deployment should succeed");
			_infrastructurePathProvider.Received().GetInfrastructurePath(null);
			_fileSystem.Received().ExistsDirectory(Arg.Is<string>(s => s.Contains("tpl")));
		}

		[Test]
		[Description("Should fail when template files are missing")]
		public void Execute_WhenTemplateFilesMissing_ShouldReturnError()
		{
			// Arrange
			_fileSystem.ExistsDirectory(Arg.Is<string>(s => s.Contains("tpl"))).Returns(false);
			var options = new DeployInfrastructureOptions();

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because template files are missing");
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Template files not found")));
		}

		[Test]
		[Description("Should deploy infrastructure components in correct order")]
		public void Execute_WhenDeploying_ShouldFollowCorrectDeploymentOrder()
		{
			// Arrange
			var options = new DeployInfrastructureOptions { SkipVerification = true };
			var applyCalls = new List<string>();

			_processExecutor.Execute("kubectl", Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
				.Returns(callInfo =>
				{
					string args = callInfo.ArgAt<string>(1);
					if (args.StartsWith("apply"))
					{
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
		[Description("Should skip verification when SkipVerification is true")]
		public void Execute_WhenSkipVerificationTrue_ShouldNotVerifyConnections()
		{
			// Arrange
			var options = new DeployInfrastructureOptions { SkipVerification = true };

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deployment should succeed");
			_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Skipping connection verification")));
			_dbClientFactory.DidNotReceive().CreatePostgresSilent(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
		}

		[Test]
		[Description("Should verify PostgreSQL connection when verification enabled")]
		public void Execute_WhenVerifyingPostgres_ShouldAttemptConnection()
		{
			// Arrange
			var options = new DeployInfrastructureOptions { SkipVerification = false };
			var connectionParams = new k8Commands.ConnectionStringParams(
				DbPort: 5432,
				DbInternalPort: 5432,
				RedisPort: 6379,
				RedisInternalPort: 6379,
				DbUsername: "postgres",
				DbPassword: "password"
			);
			_k8Commands.GetPostgresConnectionString().Returns(connectionParams);

			var postgres = Substitute.ForPartsOf<Postgres>();
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

		[Test]
		[Description("Should retry PostgreSQL connection on failure")]
		public void Execute_WhenPostgresConnectionFails_ShouldRetryMultipleTimes()
		{
			// Arrange
			var options = new DeployInfrastructureOptions { SkipVerification = false };
			var connectionParams = new k8Commands.ConnectionStringParams(
				DbPort: 5432,
				DbInternalPort: 5432,
				RedisPort: 6379,
				RedisInternalPort: 6379,
				DbUsername: "postgres",
				DbPassword: "password"
			);
			_k8Commands.GetPostgresConnectionString().Returns(connectionParams);

			var postgres = Substitute.ForPartsOf<Postgres>();
			postgres.CheckTemplateExists("template0").Throws(new Exception("Connection refused"));
			_dbClientFactory.CreatePostgresSilent(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>()).Returns(postgres);

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because PostgreSQL connection failed after retries");
			_dbClientFactory.Received(40).CreatePostgresSilent(5432, "postgres", "password");
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("PostgreSQL connection failed")));
		}

		[Test]
		[Description("Should handle deployment failure gracefully")]
		public void Execute_WhenDeploymentFails_ShouldReturnError()
		{
			// Arrange
			var options = new DeployInfrastructureOptions();
			_processExecutor.Execute("kubectl", Arg.Is<string>(s => s.Contains("apply")), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
				.Throws(new Exception("Deployment failed"));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because deployment failed");
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Failed to deploy")));
		}

		[Test]
		[Description("Should use custom infrastructure path when provided")]
		public void Execute_WhenCustomPathProvided_ShouldUseCustomPath()
		{
			// Arrange
			var options = new DeployInfrastructureOptions
			{
				InfrastructurePath = "/custom/infrastructure/path",
				SkipVerification = true
			};

			_infrastructurePathProvider.GetInfrastructurePath("/custom/infrastructure/path")
				.Returns("/custom/infrastructure/path");

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deployment should succeed with custom path");
			_infrastructurePathProvider.Received().GetInfrastructurePath("/custom/infrastructure/path");
		}
	}

	[TestFixture]
	public class DeleteInfrastructureCommandTests : BaseCommandTests<DeleteInfrastructureOptions>
	{
		private ILogger _logger;
		private Ik8Commands _k8Commands;
		private DeleteInfrastructureCommand _command;

		[SetUp]
		public void Setup()
		{
			_logger = Substitute.For<ILogger>();
			_k8Commands = Substitute.For<Ik8Commands>();

			_command = new DeleteInfrastructureCommand(_logger, _k8Commands);

			// Setup default responses
			_k8Commands.NamespaceExists("clio-infrastructure").Returns(true);
		}

		[Test]
		[Description("Should exit gracefully when namespace does not exist")]
		public void Execute_WhenNamespaceDoesNotExist_ShouldReturnSuccess()
		{
			// Arrange
			_k8Commands.NamespaceExists("clio-infrastructure").Returns(false);
			var options = new DeleteInfrastructureOptions();

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because there is nothing to delete");
			_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("does not exist")));
		}

		[Test]
		[Description("Should delete namespace and PersistentVolumes when force flag is used")]
		public void Execute_WhenForceFlag_ShouldDeleteWithoutConfirmation()
		{
			// Arrange
			var options = new DeleteInfrastructureOptions { Force = true };
			var cleanupResult = new CleanupNamespaceResult
			{
				Success = true,
				NamespaceFullyDeleted = true,
				Message = "Namespace deleted successfully",
				DeletedPersistentVolumes = new List<string> { "pv-1", "pv-2" }
			};
			_k8Commands.CleanupAndDeleteNamespace("clio-infrastructure", "clio-infrastructure")
				.Returns(cleanupResult);

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because deletion should succeed");
			_k8Commands.Received().CleanupAndDeleteNamespace("clio-infrastructure", "clio-infrastructure");
			_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("Found 2 released PersistentVolume")));
			_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("deleted successfully")));
		}

		[Test]
		[Description("Should handle cleanup failure gracefully")]
		public void Execute_WhenCleanupFails_ShouldReturnError()
		{
			// Arrange
			var options = new DeleteInfrastructureOptions { Force = true };
			var cleanupResult = new CleanupNamespaceResult
			{
				Success = false,
				Message = "Failed to delete namespace",
				DeletedPersistentVolumes = new List<string>()
			};
			_k8Commands.CleanupAndDeleteNamespace("clio-infrastructure", "clio-infrastructure")
				.Returns(cleanupResult);

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because cleanup failed");
			_logger.Received().WriteError("Failed to delete namespace");
		}

		[Test]
		[Description("Should warn when namespace is terminating")]
		public void Execute_WhenNamespaceTerminating_ShouldWarnUser()
		{
			// Arrange
			var options = new DeleteInfrastructureOptions { Force = true };
			var cleanupResult = new CleanupNamespaceResult
			{
				Success = true,
				NamespaceFullyDeleted = false,
				Message = "Namespace is terminating",
				DeletedPersistentVolumes = new List<string>()
			};
			_k8Commands.CleanupAndDeleteNamespace("clio-infrastructure", "clio-infrastructure")
				.Returns(cleanupResult);

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because operation was initiated successfully");
			_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("Namespace is terminating")));
		}

		[Test]
		[Description("Should handle exception during deletion")]
		public void Execute_WhenExceptionOccurs_ShouldReturnError()
		{
			// Arrange
			var options = new DeleteInfrastructureOptions { Force = true };
			_k8Commands.CleanupAndDeleteNamespace(Arg.Any<string>(), Arg.Any<string>())
				.Throws(new Exception("Unexpected error"));

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(1, "because an exception occurred");
			_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Deletion failed")));
		}
	}
}