using System;
using System.IO;
using System.Linq;
using ClioRing;
using ClioRing.Ipc;
using ClioRing.Models;
using ClioRing.Services;
using ClioRing.ViewModels;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for story 11: the ring settings default to the normal clio, an explicit dev-clio path
/// override round-trips through <c>app-settings.json</c> and drives clio-path resolution, a bad override
/// surfaces a friendly (jargon-free) message without persisting, and the connected-clio identity string
/// reflects normal-vs-dev-override plus the handshake version/path. All I/O is confined to a per-test temp
/// directory; no clio process is launched.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class ClioSettingsViewModelTests {
	private string _tempDir = null!;
	private string _settingsPath = null!;
	private string _devClioDll = null!;
	private string _devClioExe = null!;
	private string _notClioFile = null!;
	private ClioSettingsStore _store = null!;

	[SetUp]
	public void SetUp() {
		_tempDir = Path.Combine(Path.GetTempPath(), "clio-ring-settings-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
		_settingsPath = Path.Combine(_tempDir, "app-settings.json");
		_devClioDll = Path.Combine(_tempDir, "clio.dll");
		_devClioExe = Path.Combine(_tempDir, "clio.exe");
		_notClioFile = Path.Combine(_tempDir, "notes.txt");
		File.WriteAllText(_devClioDll, string.Empty);
		File.WriteAllText(_devClioExe, string.Empty);
		File.WriteAllText(_notClioFile, string.Empty);
		_store = new ClioSettingsStore(_settingsPath);
	}

	[TearDown]
	public void TearDown() {
		try {
			if (Directory.Exists(_tempDir)) {
				Directory.Delete(_tempDir, recursive: true);
			}
		}
		catch (IOException) {
			// Best-effort temp cleanup; never fail a test on a locked handle.
		}
	}

	[Test]
	[Description("A valid dev-clio override is persisted to app-settings.json and then resolves the launch settings to that dev build (dll driven by dotnet).")]
	public void SaveDevClioOverride_ShouldPersistAndResolveToDevClio_WhenPathIsValid() {
		// Arrange
		var sut = new ClioSettingsViewModel(_store);
		sut.DevClioPathInput = _devClioDll;

		// Act
		sut.SaveDevClioOverrideCommand.Execute(null);
		string? persisted = _store.ReadDevClioPath();
		ClioIpcSettings resolved = Startup.ResolveClioIpcSettings(persisted, null);

		// Assert
		sut.HasValidationError.Should().BeFalse("because a real clio.dll passes validation");
		sut.IsDevOverrideActive.Should().BeTrue("because a valid dev override is now active");
		persisted.Should().Be(_devClioDll, "because the override round-trips through app-settings.json");
		resolved.Command.Should().Be("dotnet", "because a .dll dev build is launched via dotnet");
		resolved.Args.Should().HaveCount(2, "because resolution passes the dev dll then the mcp-server verb");
		resolved.Args[0].Should().Be(_devClioDll, "because the launch points at the dev dll");
		resolved.Args[1].Should().Be("mcp-server", "because the dll is run as the mcp-server");
	}

	[Test]
	[Description("A blank override clears the persisted value and resolution falls back to the machine default clio.")]
	public void SaveDevClioOverride_ShouldClearAndResolveToDefault_WhenPathIsBlank() {
		// Arrange
		_store.SaveDevClioPath(_devClioDll);
		var sut = new ClioSettingsViewModel(_store);
		sut.DevClioPathInput = "   ";

		// Act
		sut.SaveDevClioOverrideCommand.Execute(null);
		string? persisted = _store.ReadDevClioPath();
		ClioIpcSettings resolved = Startup.ResolveClioIpcSettings(persisted, null);

		// Assert
		persisted.Should().BeNull("because a blank override removes the persisted value");
		sut.IsDevOverrideActive.Should().BeFalse("because no override is active after clearing");
		resolved.Should().BeSameAs(ClioIpcSettings.Default, "because empty override resolves to the default clio");
		resolved.Command.Should().Be("clio", "because Release uses the installed dotnet tool from PATH");
		resolved.Args.Should().ContainSingle().Which.Should().Be("mcp-server",
			"because Ring starts the installed tool as an MCP server");
	}

	[Test]
	[Description("An explicit Release selection uses the installed clio command while preserving a valid development target for later use.")]
	public void ResolveClioIpcSettings_ShouldUseInstalledTool_WhenReleaseSelected() {
		// Arrange
		var explicitIpc = new ClioIpcSettingsDto {
			Command = "dotnet", Args = [_devClioDll, "mcp-server"]
		};

		// Act
		ClioIpcSettings resolved = Startup.ResolveClioIpcSettings("release", _devClioDll, explicitIpc);

		// Assert
		resolved.Should().BeSameAs(ClioIpcSettings.Default,
			"because Release ignores the saved development targets without deleting them");
		resolved.Command.Should().Be("clio", "because the released dotnet tool is resolved from PATH");
	}

	[Test]
	[Description("Legacy settings with an explicit clio IPC target remain on Development when no runtime mode has been persisted.")]
	public void ResolveClioIpcSettings_ShouldPreserveDevelopmentTarget_WhenLegacyModeIsMissing() {
		// Arrange
		var explicitIpc = new ClioIpcSettingsDto {
			Command = "dotnet", Args = [_devClioDll, "mcp-server"]
		};

		// Act
		ClioIpcSettings resolved = Startup.ResolveClioIpcSettings(runtimeMode: null, devClioPath: null,
			explicitIpc);

		// Assert
		resolved.Command.Should().Be("dotnet",
			"because migration must not silently retarget an existing development setup");
		resolved.Args.Should().Contain(_devClioDll,
			"because the saved explicit development target remains selected");
	}

	[Test]
	[Description("An override path that does not exist is rejected with a friendly, jargon-free message and is NOT persisted.")]
	public void SaveDevClioOverride_ShouldSurfaceFriendlyMessageAndNotPersist_WhenPathIsMissing() {
		// Arrange
		var sut = new ClioSettingsViewModel(_store);
		sut.DevClioPathInput = Path.Combine(_tempDir, "does-not-exist", "clio.dll");

		// Act
		sut.SaveDevClioOverrideCommand.Execute(null);

		// Assert
		sut.HasValidationError.Should().BeTrue("because the file does not exist");
		sut.ValidationMessage.Should().Contain("clio",
			"because the friendly message tells the user to point at a clio build");
		sut.ValidationMessage.Should().NotContain("MCP", "because the message must stay free of protocol jargon");
		_store.ReadDevClioPath().Should().BeNull("because an invalid override must not be persisted");
	}

	[Test]
	[Description("An existing file that is not a clio build is rejected with a friendly message and is not persisted.")]
	public void SaveDevClioOverride_ShouldReject_WhenFileExistsButIsNotClio() {
		// Arrange
		var sut = new ClioSettingsViewModel(_store);
		sut.DevClioPathInput = _notClioFile;

		// Act
		sut.SaveDevClioOverrideCommand.Execute(null);

		// Assert
		sut.HasValidationError.Should().BeTrue("because the file exists but is not clio.dll/clio.exe");
		sut.ValidationMessage.Should().Contain("clio", "because the message names the expected clio build");
		_store.ReadDevClioPath().Should().BeNull("because a non-clio file must not be persisted");
	}

	[Test]
	[Description("A validated .exe dev override resolves to launching the exe directly with the mcp-server verb.")]
	public void ResolveClioIpcSettings_ShouldLaunchExeDirectly_WhenOverrideIsExe() {
		// Arrange
		_store.SaveDevClioPath(_devClioExe);

		// Act
		ClioIpcSettings resolved = Startup.ResolveClioIpcSettings(_store.ReadDevClioPath(), null);

		// Assert
		resolved.Command.Should().Be(_devClioExe, "because a .exe dev build is launched directly");
		resolved.Args.Should().ContainSingle().Which.Should().Be("mcp-server",
			"because the exe is invoked with only the mcp-server verb");
	}

	[Test]
	[Description("Refresh loads the persisted valid override into the editable field and marks the override active.")]
	public void Refresh_ShouldLoadPersistedOverride_WhenOverrideConfigured() {
		// Arrange
		_store.SaveDevClioPath(_devClioDll);

		// Act
		var sut = new ClioSettingsViewModel(_store); // ctor calls Refresh()

		// Assert
		sut.DevClioPathInput.Should().Be(_devClioDll, "because Refresh loads the persisted override into the editor");
		sut.IsDevOverrideActive.Should().BeTrue("because the persisted override is a valid clio build");
	}

	[Test]
	[Description("A persisted-but-invalid override (e.g. hand-edited into the file) is flagged as ignored and not treated as an active override, so the fallback to normal clio is not silent.")]
	public void Refresh_ShouldFlagInvalidPersistedOverride_WhenFileHandEditedToBadPath() {
		// Arrange
		_store.SaveDevClioPath(Path.Combine(_tempDir, "gone", "clio.dll")); // bypasses VM validation

		// Act
		var sut = new ClioSettingsViewModel(_store); // ctor Refresh() re-validates the persisted value

		// Assert
		sut.HasValidationError.Should().BeTrue("because the persisted override no longer points at a clio build");
		sut.IsDevOverrideActive.Should().BeFalse("because an invalid override is not an active dev override");
	}

	[Test]
	[Description("The connected-clio identity reflects the dev-override source together with the handshake name/version and the resolved path.")]
	public void SetConnectedIdentity_ShouldReflectDevOverrideAndHandshake_WhenConnectedToDevBuild() {
		// Arrange
		var sut = new ClioSettingsViewModel(_store);
		var handshake = new ClioServerHandshake {
			ServerName = "clio", ServerVersion = "8.1.0.77", Capabilities = new[] { "tools" }
		};

		// Act
		sut.SetConnectedIdentity(handshake, _devClioDll, isDevOverride: true);

		// Assert
		sut.ConnectedClioIdentity.Should().Contain("clio 8.1.0.77",
			"because the identity uses the handshake name + version, not a hardcoded label");
		sut.ConnectedClioIdentity.Should().Contain("development build",
			"because the development override is the active source");
		sut.ConnectedClioIdentity.Should().Contain(_devClioDll, "because the resolved path is shown");
	}

	[Test]
	[Description("With no override, the connected-clio identity reflects the normal build alongside the handshake identity.")]
	public void SetConnectedIdentity_ShouldReflectNormalBuild_WhenNoOverride() {
		// Arrange
		var sut = new ClioSettingsViewModel(_store);
		var handshake = new ClioServerHandshake {
			ServerName = "clio", ServerVersion = "8.1.0.77", Capabilities = new[] { "tools" }
		};

		// Act
		sut.SetConnectedIdentity(handshake, @"C:\Tools\clio\clio.dll", isDevOverride: false);

		// Assert
		sut.ConnectedClioIdentity.Should().Contain("release build", "because no development override is active");
		sut.ConnectedClioIdentity.Should().Contain("8.1.0.77", "because the handshake version is surfaced");
	}

	[Test]
	[Description("Before a handshake exists the identity states the configured build instead of claiming Ring is disconnected.")]
	public void SetConnectedIdentity_ShouldStateConfiguredBuild_WhenHandshakeAbsent() {
		// Arrange
		var sut = new ClioSettingsViewModel(_store);

		// Act
		sut.SetConnectedIdentity(handshake: null, @"C:\Tools\clio\clio.dll", isDevOverride: false);

		// Assert
		sut.ConnectedClioIdentity.Should().Contain("release build",
			"because the configured runtime is truthful before the lazy handshake occurs");
		sut.ConnectedClioIdentity.Should().NotContain("not connected",
			"because Ring already knows which configured runtime it is using");
		sut.ConnectedClioIdentity.Should().NotContain("—",
			"because runtime identity uses ordinary hyphens instead of em dashes");
		sut.ConnectedClioIdentity.Should().NotContain("MCP", "because the identity must stay jargon-free");
	}

	[Test]
	[Description("The main runtime warning recognizes an explicit Debug DLL target and selecting Release persists the mode without deleting that target.")]
	public void RuntimeSwitch_ShouldPersistReleaseAndPreserveTarget_WhenDevelopmentRuntimeIsRunning() {
		// Arrange
		File.WriteAllText(_settingsPath, $$"""
			{
			  "WorkspaceFolder": "{{_tempDir.Replace("\\", "\\\\")}}",
			  "ClioIpc": {
			    "Command": "dotnet",
			    "Args": ["{{_devClioDll.Replace("\\", "\\\\")}}", "mcp-server"]
			  }
			}
			""");
		IClioIpcClient client = Substitute.For<IClioIpcClient>();
		client.TargetPath.Returns(_devClioDll);
		client.Handshake.Returns((ClioServerHandshake?)null);
		var sut = new ClioSettingsViewModel(_store, client);

		// Act
		sut.IsDevelopmentSelected = false;

		// Assert
		sut.IsDevelopmentRuntimeRunning.Should().BeTrue(
			"because an explicit Debug DLL is a development runtime even without DevClioPath");
		sut.RuntimeSummary.Should().Be("Ring is using a development clio build",
			"because lazy handshake state must not be presented as disconnected");
		_store.ReadRuntimeMode().Should().Be("release",
			"because the main toggle persists the next-launch runtime mode");
		_store.HasDevelopmentTarget().Should().BeTrue(
			"because selecting Release preserves the explicit development target");
		sut.RuntimeSelectionRestartRequired.Should().BeTrue(
			"because the running development child remains active until Ring restarts");
	}
}
