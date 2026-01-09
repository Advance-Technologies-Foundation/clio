using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.Common.K8;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class DeleteInfrastructureCommandTests : BaseCommandTests<DeleteInfrastructureOptions>{
	#region Fields: Private

	private DeleteInfrastructureCommand _command;
	private Ik8Commands _k8Commands;
	private ILogger _logger;

	#endregion

	#region Methods: Public

	[Test]
	[Description("Should handle cleanup failure gracefully")]
	public void Execute_WhenCleanupFails_ShouldReturnError() {
		// Arrange
		DeleteInfrastructureOptions options = new() { Force = true };
		CleanupNamespaceResult cleanupResult = new() {
			Success = false, Message = "Failed to delete namespace", DeletedPersistentVolumes = new List<string>()
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
	[Description("Should handle exception during deletion")]
	public void Execute_WhenExceptionOccurs_ShouldReturnError() {
		// Arrange
		DeleteInfrastructureOptions options = new() { Force = true };
		_k8Commands.CleanupAndDeleteNamespace(Arg.Any<string>(), Arg.Any<string>())
				   .Throws(new Exception("Unexpected error"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because an exception occurred");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Error deleting namespace")));
	}

	[Test]
	[Description("Should delete namespace and PersistentVolumes when force flag is used")]
	public void Execute_WhenForceFlag_ShouldDeleteWithoutConfirmation() {
		// Arrange
		DeleteInfrastructureOptions options = new() { Force = true };
		CleanupNamespaceResult cleanupResult = new() {
			Success = true, NamespaceFullyDeleted = true, Message = "Namespace deleted successfully",
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
	[Description("Should exit gracefully when namespace does not exist")]
	public void Execute_WhenNamespaceDoesNotExist_ShouldReturnSuccess() {
		// Arrange
		_k8Commands.NamespaceExists("clio-infrastructure").Returns(false);
		DeleteInfrastructureOptions options = new();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because there is nothing to delete");
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("does not exist")));
	}

	[Test]
	[Description("Should warn when namespace is terminating")]
	public void Execute_WhenNamespaceTerminating_ShouldWarnUser() {
		// Arrange
		DeleteInfrastructureOptions options = new() { Force = true };
		CleanupNamespaceResult cleanupResult = new() {
			Success = true, NamespaceFullyDeleted = false, Message = "Namespace is terminating",
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

	[SetUp]
	public override void Setup() {
		_logger = Substitute.For<ILogger>();
		_k8Commands = Substitute.For<Ik8Commands>();

		_command = new DeleteInfrastructureCommand(_logger, _k8Commands);

		// Setup default responses
		_k8Commands.NamespaceExists("clio-infrastructure").Returns(true);
	}

	#endregion
}
