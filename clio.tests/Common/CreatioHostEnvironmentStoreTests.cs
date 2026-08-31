using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class CreatioHostEnvironmentStoreTests : BaseClioModuleTests
{
	private IFileSystem _fileSystem;
	private IFileSecurityHardening _fileSecurityHardening;
	private CreatioHostEnvironmentStore _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		_fileSystem = Substitute.For<IFileSystem>();
		_fileSecurityHardening = Substitute.For<IFileSecurityHardening>();
		containerBuilder.AddSingleton(_fileSystem);
		containerBuilder.AddSingleton(_fileSecurityHardening);
		containerBuilder.AddTransient<CreatioHostEnvironmentStore>();
	}

	[SetUp]
	public void SetUp()
	{
		_sut = Container.GetRequiredService<CreatioHostEnvironmentStore>();
	}

	[TearDown]
	public override void TearDown()
	{
		_fileSystem.ClearReceivedCalls();
		_fileSecurityHardening.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Stores host environment values in the protected per-user Clio state rather than the deployed application directory.")]
	public void Save_ShouldWriteOwnerOnlyEnvironmentFileOutsideApplicationDirectory()
	{
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string>
		{
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		_sut.Save("/tmp/creatio", environmentVariables);

		// Assert
		_fileSystem.Received(1).CreateDirectoryIfNotExists(
			Arg.Is<string>(path => path.StartsWith(ClioRuntimePaths.Home, System.StringComparison.Ordinal)));
		_fileSystem.Received(1).WriteOwnerOnlyTextToFile(
			Arg.Is<string>(path => path.StartsWith(ClioRuntimePaths.Home, System.StringComparison.Ordinal)
				&& !path.StartsWith("/tmp/creatio", System.StringComparison.Ordinal)),
			Arg.Is<string>(json => json.Contains("Kestrel__Endpoints__Https__Certificate__Password", System.StringComparison.Ordinal)
				&& json.Contains("secret", System.StringComparison.Ordinal)));
		_fileSecurityHardening.Received(1).HardenDirectory(Arg.Any<string>());
		_fileSecurityHardening.Received(1).HardenFile(Arg.Any<string>());
	}

	[Test]
	[Description("Loads the saved host environment values with case-insensitive variable lookup for a later dotnet start.")]
	public void Load_ShouldReturnSavedEnvironmentValues()
	{
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(
			"{\"Kestrel__Endpoints__Https__Certificate__Password\":\"secret\"}");

		// Act
		IReadOnlyDictionary<string, string> result = _sut.Load("/tmp/creatio");

		// Assert
		result["kestrel__endpoints__https__certificate__password"].Should().Be("secret",
			because: "a later host start must restore the certificate environment value independent of JSON key casing");
	}

	[Test]
	[Description("Wraps case-variant duplicate environment keys as a safe invalid-store error instead of leaking a dictionary-construction exception.")]
	public void Load_ShouldWrapCaseVariantDuplicateKeys()
	{
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns("{\"Key\":\"first\",\"key\":\"second\"}");

		// Act
		Action action = () => _sut.Load("/tmp/creatio");

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The saved Creatio host environment is invalid or cannot be read: *.",
				because: "case-insensitive environment lookup must reject ambiguous persisted values with the documented diagnostic");
	}

	[Test]
	[Description("Removes stale host environment values when a deployment no longer needs certificate secrets.")]
	public void Save_ShouldDeleteStoreFile_WhenEnvironmentIsEmpty()
	{
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string>();

		// Act
		_sut.Save("/tmp/creatio", environmentVariables);

		// Assert
		_fileSystem.Received(1).DeleteFileIfExists(
			Arg.Is<string>(path => path.StartsWith(ClioRuntimePaths.Home, System.StringComparison.Ordinal)));
		_fileSystem.DidNotReceive().WriteOwnerOnlyTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}
}
