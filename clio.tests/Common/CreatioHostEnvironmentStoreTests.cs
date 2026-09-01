using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	[Description("Accepts a Kestrel endpoint name containing a hyphen when persisting its certificate password")]
	public void Save_ShouldAcceptHyphenatedKestrelEndpointName()
	{
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string>
		{
			["Kestrel__Endpoints__https-prod__Certificate__Password"] = "secret"
		};

		// Act
		_sut.Save("/tmp/creatio", environmentVariables);

		// Assert
		_fileSystem.Received(1).WriteOwnerOnlyTextToFile(
			Arg.Any<string>(),
			Arg.Is<string>(json => json.Contains("https-prod", StringComparison.Ordinal)));
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
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(
			"{\"Kestrel__Certificates__Default__Password\":\"first\","
			+ "\"kestrel__certificates__default__password\":\"second\"}");

		// Act
		Action action = () => _sut.Load("/tmp/creatio");

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("The saved Creatio host environment is invalid or cannot be read: *.",
				because: "case-insensitive environment lookup must reject ambiguous persisted values with the documented diagnostic");
	}

	[Test]
	[Description("Rejects arbitrary process environment variables so a tampered host store cannot inject dotnet startup or path settings into a later start.")]
	public void Save_ShouldRejectNonCertificateEnvironmentVariables()
	{
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string>
		{
			["DOTNET_STARTUP_HOOKS"] = "/tmp/attacker.dll"
		};

		// Act
		Action action = () => _sut.Save("/tmp/creatio", environmentVariables);

		// Assert
		action.Should().Throw<ArgumentException>(because:
			"the persisted store is only trusted for the certificate password values generated by dotnet deployment");
		_fileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IFileSystem.WriteOwnerOnlyTextToFile))
			.Should().BeEmpty(
				because: "an invalid persisted environment map must not be written to the protected store");
	}

	[Test]
	[Description("Rejects a persisted arbitrary environment variable instead of restoring it into a future dotnet process.")]
	public void Load_ShouldRejectNonCertificateEnvironmentVariables()
	{
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.ReadAllText(Arg.Any<string>()).Returns(
			"{\"DOTNET_STARTUP_HOOKS\":\"/tmp/attacker.dll\"}");

		// Act
		Action action = () => _sut.Load("/tmp/creatio");

		// Assert
		action.Should().Throw<InvalidOperationException>(because:
			"a corrupted or tampered store must fail closed before values reach the dotnet child process");
	}

	[Test]
	[Description("Rejects a symbolic-link store directory before a certificate password can be written outside the protected Clio state directory.")]
	public void Save_ShouldRejectSymbolicLinkStoreDirectory()
	{
		// Arrange
		System.IO.Abstractions.IDirectoryInfo link =
			Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		link.LinkTarget.Returns("/tmp/attacker-target");
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_fileSystem.GetDirectoryInfo(Arg.Any<string>()).Returns(link);
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string>
		{
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		Action action = () => _sut.Save("/tmp/creatio", environmentVariables);

		// Assert
		action.Should().Throw<IOException>(because:
			"the store must never follow a planted link to an attacker-controlled directory");
		_fileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IFileSystem.WriteOwnerOnlyTextToFile))
			.Should().BeEmpty(
				because: "a symbolic-link store directory must be rejected before any secret write is attempted");
	}

	[Test]
	[Description("Rejects a symbolic-link store file before a certificate password can be read from an unrelated target.")]
	public void Load_ShouldRejectSymbolicLinkStoreFile()
	{
		// Arrange
		System.IO.Abstractions.IFileInfo link =
			Substitute.For<System.IO.Abstractions.IFileInfo>();
		link.LinkTarget.Returns("/tmp/attacker-target");
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);
		_fileSystem.GetFilesInfos(Arg.Any<string>()).Returns(link);

		// Act
		Action action = () => _sut.Load("/tmp/creatio");

		// Assert
		action.Should().Throw<InvalidOperationException>(because:
			"a persisted certificate password must not be read through a planted symbolic link");
		_fileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IFileSystem.ReadAllText))
			.Should().BeEmpty(
				because: "a symbolic-link store file must be rejected before its contents are read");
	}

	[Test]
	[Description("Rejects a symbolic-link store file before empty-state cleanup can delete through an attacker-controlled directory.")]
	public void Save_ShouldRejectSymbolicLinkStoreFile_WhenEnvironmentIsEmpty()
	{
		// Arrange
		System.IO.Abstractions.IFileInfo link =
			Substitute.For<System.IO.Abstractions.IFileInfo>();
		link.LinkTarget.Returns("/tmp/attacker-target");
		_fileSystem.GetFilesInfos(Arg.Any<string>()).Returns(link);

		// Act
		Action action = () => _sut.Save("/tmp/creatio", new Dictionary<string, string>());

		// Assert
		action.Should().Throw<IOException>(because:
			"empty-state cleanup must fail closed when the store file path is a planted symbolic link");
		_fileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IFileSystem.DeleteFileIfExists))
			.Should().BeEmpty(
				because: "cleanup must not follow or remove an untrusted symbolic-link path");
	}

	[Test]
	[Description("Removes a partially written host environment file when ownership hardening fails after the secret is written.")]
	public void Save_ShouldDeleteStoreFile_WhenFileHardeningFails()
	{
		// Arrange
		_fileSecurityHardening.WhenForAnyArgs(hardening => hardening.HardenFile(default!))
			.Do(_ => throw new IOException("ACL update failed"));
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string>
		{
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		Action action = () => _sut.Save("/tmp/creatio", environmentVariables);

		// Assert
		action.Should().Throw<IOException>(because:
			"deployment must fail when the certificate password store cannot be protected");
		_fileSystem.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IFileSystem.DeleteFileIfExists)
				&& call.GetArguments()[0] is string path
				&& path.StartsWith(ClioRuntimePaths.Home, StringComparison.Ordinal))
			.Should().ContainSingle(
				because: "a failed hardening operation must not leave a plaintext certificate password file behind");
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
