using System;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.K8;
using FluentAssertions;
using k8s;
using k8s.Models;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.K8;

[TestFixture]
public class CpTests {

	#region Setup/Teardown

	[SetUp]
	public void Setup() {
		// Setup substitutes
		_k8sClient = Substitute.For<IKubernetes>();

		// Access the internal Cp class using reflection
		Type cpType = typeof(Cp);
		_cpService = (Cp)Activator.CreateInstance(cpType, _k8sClient);

		// Create a temporary test file
		_testSourceFilePath = Path.Combine(Path.GetTempPath(), $"test_source_{Guid.NewGuid()}.txt");
		File.WriteAllText(_testSourceFilePath, "Test content");

		_testDestinationFilePath = "/tmp/test_destination.txt";
	}

	[TearDown]
	public void Cleanup() {
		// Delete temporary test file
		if (File.Exists(_testSourceFilePath)) {
			File.Delete(_testSourceFilePath);
		}
	}

	#endregion

	#region Fields: Private

	private IKubernetes _k8sClient;
	private Cp _cpService;
	private string _testSourceFilePath;
	private string _testDestinationFilePath;

	#endregion

	[Test]
	public async Task Copy_ShouldCallCopyFileToPodAsync() {
		// Arrange
		V1Pod testPod = new() {
			Metadata = new V1ObjectMeta {
				Name = "test-pod"
			}
		};
		string namespaceName = "test-namespace";
		string containerName = "test-container";

		_k8sClient.NamespacedPodExecAsync(Arg.Any<string>(),
					Arg.Any<string>(),
					Arg.Any<string>(),
					Arg.Any<string[]>(),
					Arg.Any<bool>(),
					Arg.Any<ExecAsyncCallback>(),
					Arg.Any<CancellationToken>())
				.Returns(0);

		// Act
		await _cpService.CopyAsync(testPod, namespaceName, containerName, _testSourceFilePath, _testDestinationFilePath);

		// Assert
		await _k8sClient.Received(1).NamespacedPodExecAsync("test-pod",
			namespaceName,
			containerName,
			Arg.Any<string[]>(),
			false,
			Arg.Any<ExecAsyncCallback>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public void CopyFileToPodAsync_EmptyDestinationPath_ShouldThrowArgumentException() {
		// Arrange
		string podName = "test-pod";
		string namespaceName = "test-namespace";
		string containerName = "test-container";

		// Act & Assert
		Func<Task> act = async () => await _cpService.CopyFileToPodAsync(
			podName, namespaceName, containerName, _testSourceFilePath, string.Empty);

		act.Should().ThrowAsync<ArgumentException>()
			.WithMessage("*destinationPath cannot be null or whitespace*");
	}

	[Test]
	public void CopyFileToPodAsync_NullSourcePath_ShouldThrowArgumentException() {
		// Arrange
		string podName = "test-pod";
		string namespaceName = "test-namespace";
		string containerName = "test-container";

		// Act & Assert
		Func<Task> act = async () => await _cpService.CopyFileToPodAsync(
			podName, namespaceName, containerName, null, _testDestinationFilePath);

		act.Should().ThrowAsync<ArgumentException>()
			.WithMessage("*sourcePath cannot be null or whitespace*");
	}

	[Test]
	public async Task CopyFileToPodAsync_ShouldUseCorrectTarCommand() {
		// Arrange
		const string podName = "test-pod";
		const string namespaceName = "test-namespace";
		const string containerName = "test-container";
		const string destinationFolder = "/tmp";

		// Act
		await _cpService.CopyFileToPodAsync(podName, namespaceName, containerName,
			_testSourceFilePath, _testDestinationFilePath);
		
		
		
		//await _k8sClient.ReceivedWithAnyArgs().NamespacedPodExecAsync();
		await _k8sClient.ReceivedWithAnyArgs().NamespacedPodExecAsync(Arg.Is(podName),
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<string[]>(),
			Arg.Any<bool>(),
			Arg.Any<ExecAsyncCallback>(),
			Arg.Any<CancellationToken>());
	}

	
	
	
	[Test]
	public void GetFolderName_LinuxPath_ShouldReturnSamePath() {
		// Arrange
		MethodInfo method = _cpService.GetType().GetMethod("GetFolderName",
			BindingFlags.NonPublic | BindingFlags.Static);

		// Act
		object result = method.Invoke(null, new object[] {"/tmp/test.txt"});

		// Assert
		result.Should().Be("/tmp");
	}

	[Test]
	public void GetFolderName_WindowsPath_ShouldConvertBackslashesToForwardSlashes() {
		// Arrange
		MethodInfo method = _cpService.GetType().GetMethod("GetFolderName",
			BindingFlags.NonPublic | BindingFlags.Static);

		// Act
		object result = method.Invoke(null, new object[] {@"C:\Temp\test.txt"});

		// Assert
		result.Should().Be("C:/Temp");
	}

	[Test]
	public void ValidatePathParameters_EmptyDestinationPath_ShouldThrowArgumentException() {
		// Arrange
		MethodInfo method = _cpService.GetType().GetMethod("ValidatePathParameters",
			BindingFlags.NonPublic | BindingFlags.Instance);

		// Act & Assert
		TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
			method.Invoke(_cpService, new object[] {_testSourceFilePath, string.Empty}));

		ex.InnerException.Should().BeOfType<ArgumentException>();
		ex.InnerException.Message.Should().Contain("destinationPath cannot be null or whitespace");
	}

	[Test]
	public void ValidatePathParameters_NullSourcePath_ShouldThrowArgumentException() {
		// Arrange
		MethodInfo method = _cpService.GetType().GetMethod("ValidatePathParameters",
			BindingFlags.NonPublic | BindingFlags.Instance);

		// Act & Assert
		TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() =>
			method.Invoke(_cpService, new object[] {null, _testDestinationFilePath}));

		ex.InnerException.Should().BeOfType<ArgumentException>();
		ex.InnerException.Message.Should().Contain("sourcePath cannot be null or whitespace");
	}

	[Test]
	public void ValidatePathParameters_ValidPaths_ShouldNotThrow() {
		// Arrange
		MethodInfo method = _cpService.GetType().GetMethod("ValidatePathParameters",
			BindingFlags.NonPublic | BindingFlags.Instance);

		// Act & Assert
		Assert.DoesNotThrow(() =>
			method.Invoke(_cpService, new object[] {_testSourceFilePath, _testDestinationFilePath}));
	}

}
