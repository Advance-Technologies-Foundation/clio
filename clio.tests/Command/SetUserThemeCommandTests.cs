namespace Clio.Tests.Command;

using System;
using System.Reflection;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class SetUserThemeCommandTests : BaseCommandTests<SetUserThemeOptions>
{
	private const string ProfileId = "7f3b869f-34f3-4f20-ab4d-7480a5fdf647";

	private IApplicationClient _applicationClient;
	private ILogger _logger;
	private SetUserThemeCommand _command;

	public override void Setup() {
		base.Setup();
		// Resolve the SUT from the container so it is wired exactly as production (real IServiceUrlBuilder,
		// the shared EnvironmentSettings singleton, the injected ListThemesCommand); only the I/O boundary
		// (IApplicationClient) is faked.
		_command = Container.GetRequiredService<SetUserThemeCommand>();
		_logger = Substitute.For<ILogger>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
	}

	private void StubAvailableThemes(string valuesJson) =>
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("GetAvailableThemes")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"success\":true,\"values\":[{valuesJson}]}}");

	private void StubProfileSelect(string id, string theme) =>
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($"{{\"success\":true,\"rows\":[{{\"Id\":\"{id}\",\"Theme\":\"{theme}\"}}]}}");

	private void StubUpdateSuccess() =>
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");

	private static readonly string OceanThemeJson =
		"{\"id\":\"ocean-id\",\"caption\":\"Ocean\",\"cssClassName\":\"ocean-theme\",\"cssFilePath\":\"a/theme.css\"}";

	[Test, Category("Unit")]
	[Description("Applies a theme matched by caption: reads the current user's profile id, writes the theme's Id (the value the Shell resolves) to the SysUserProfile Theme column filtered by that id, and verifies the read-back.")]
	public void SetUserTheme_ShouldWriteResolvedThemeIdFilteredByProfileId_WhenMatchedByCaption() {
		// Arrange
		string capturedUpdateBody = null;
		StubAvailableThemes(OceanThemeJson);
		// After the write, the profile stores the theme Id, so the verification read-back returns "ocean-id".
		StubProfileSelect(ProfileId, "ocean-id");
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedUpdateBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out AppliedUserTheme applied, out string error);

		// Assert
		succeeded.Should().BeTrue(because: "the theme resolved and the read-back matched the written value");
		error.Should().BeNull(because: "a successful apply reports no error");
		applied.Id.Should().Be("ocean-id", because: "the theme is selected by its Id, which is the value written to the profile");
		applied.Caption.Should().Be("Ocean", because: "the human caption is reported back to the user");
		capturedUpdateBody.Should().Contain("\"rootSchemaName\":\"SysUserProfile\"",
			because: "the profile theme is stored on the virtual SysUserProfile entity");
		capturedUpdateBody.Should().Contain("ocean-id",
			because: "the theme Id (not the cssClassName) is the value the Shell resolves and must be written to the Theme column");
		capturedUpdateBody.Should().NotContain("ocean-theme",
			because: "writing the cssClassName would silently fall back to the default theme");
		capturedUpdateBody.Should().Contain(ProfileId,
			because: "the update must be filtered by the current user's profile id read from SysUserProfile");
	}

	[Test, Category("Unit")]
	[Description("Resolves the theme by its exact cssClassName when the argument matches no caption, and still writes the theme Id.")]
	public void SetUserTheme_ShouldResolveByCssClassName_WhenArgumentIsCssClassName() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		StubProfileSelect(ProfileId, "ocean-id");
		StubUpdateSuccess();
		SetUserThemeOptions options = new() { Theme = "ocean-theme" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out AppliedUserTheme applied, out _);

		// Assert
		succeeded.Should().BeTrue(because: "an exact cssClassName is a valid theme selector");
		applied.Id.Should().Be("ocean-id", because: "the resolved theme's Id is what gets written to the profile");
	}

	[Test, Category("Unit")]
	[Description("Resolves the theme by its exact id when the argument matches no cssClassName or caption.")]
	public void SetUserTheme_ShouldResolveById_WhenArgumentIsId() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		StubProfileSelect(ProfileId, "ocean-id");
		StubUpdateSuccess();
		SetUserThemeOptions options = new() { Theme = "ocean-id" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out AppliedUserTheme applied, out _);

		// Assert
		succeeded.Should().BeTrue(because: "an exact theme id is a valid selector");
		applied.Id.Should().Be("ocean-id", because: "the theme matched by id is applied by writing that id");
	}

	[Test, Category("Unit")]
	[Description("Reset writes an empty Theme value and verifies the profile no longer carries a theme.")]
	public void SetUserTheme_ShouldWriteEmptyTheme_WhenReset() {
		// Arrange
		string capturedUpdateBody = null;
		StubProfileSelect(ProfileId, string.Empty);
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => { capturedUpdateBody = ci.ArgAt<string>(1); return "{\"success\":true}"; });
		SetUserThemeOptions options = new() { Reset = true };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out AppliedUserTheme applied, out _);

		// Assert
		succeeded.Should().BeTrue(because: "reset clears the theme and the read-back confirms an empty value");
		applied.CssClassName.Should().BeEmpty(because: "reset applies no theme");
		capturedUpdateBody.Should().Contain("\"rootSchemaName\":\"SysUserProfile\"",
			because: "reset still targets the SysUserProfile entity");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("GetAvailableThemes")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Fails with an available-theme list and posts no UpdateQuery when the theme argument matches nothing.")]
	public void SetUserTheme_ShouldFailWithAvailableThemes_WhenThemeUnknown() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		SetUserThemeOptions options = new() { Theme = "does-not-exist" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "an unknown theme cannot be applied");
		error.Should().Contain("Ocean", because: "the error must list the themes actually available to the user");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("UpdateQuery")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Fails with the candidate theme ids and posts no UpdateQuery when a caption matches more than one theme, instead of silently applying the first match.")]
	public void SetUserTheme_ShouldFailWithCandidates_WhenCaptionIsAmbiguous() {
		// Arrange
		string secondOcean =
			"{\"id\":\"ocean-2\",\"caption\":\"Ocean\",\"cssClassName\":\"ocean-two\",\"cssFilePath\":\"b/theme.css\"}";
		StubAvailableThemes(OceanThemeJson + "," + secondOcean);
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "an ambiguous caption must not be silently resolved to the first match");
		error.Should().Contain("ocean-id", because: "the error must list the first candidate's id so the caller can disambiguate");
		error.Should().Contain("ocean-2", because: "the error must list every candidate id, not just one");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("UpdateQuery")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Resolves unambiguously by id even when two themes share the same caption, so the guidance-recommended id selector is never blocked by caption collisions.")]
	public void SetUserTheme_ShouldResolveById_WhenCaptionAmbiguousButIdSupplied() {
		// Arrange
		string secondOcean =
			"{\"id\":\"ocean-2\",\"caption\":\"Ocean\",\"cssClassName\":\"ocean-two\",\"cssFilePath\":\"b/theme.css\"}";
		StubAvailableThemes(OceanThemeJson + "," + secondOcean);
		StubProfileSelect(ProfileId, "ocean-2");
		StubUpdateSuccess();
		SetUserThemeOptions options = new() { Theme = "ocean-2" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out AppliedUserTheme applied, out _);

		// Assert
		succeeded.Should().BeTrue(because: "an exact id is unambiguous regardless of duplicate captions");
		applied.Id.Should().Be("ocean-2", because: "the theme matched by its unique id is the one applied");
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when both a theme argument and --reset are supplied.")]
	public void SetUserTheme_ShouldFailFastWithoutHttp_WhenThemeAndResetBothSupplied() {
		// Arrange
		SetUserThemeOptions options = new() { Theme = "Ocean", Reset = true };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a theme and --reset are mutually exclusive");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("not both")));
	}

	[Test, Category("Unit")]
	[Description("Fails fast without any HTTP call when neither a theme argument nor --reset is supplied.")]
	public void SetUserTheme_ShouldFailFastWithoutHttp_WhenNothingSupplied() {
		// Arrange
		SetUserThemeOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "there is nothing to apply and no reset requested");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("required")));
	}

	[Test, Category("Unit")]
	[Description("Detects the silent no-op (ChangeTheme feature disabled): the UpdateQuery reports success but the read-back shows the value did not change, so the command fails with an actionable message.")]
	public void SetUserTheme_ShouldFail_WhenReadBackShowsThemeUnchanged() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		// Server accepts the update but the listener silently ignores it, so the profile still reports no theme.
		StubProfileSelect(ProfileId, string.Empty);
		StubUpdateSuccess();
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "a write that does not change the value must not report success");
		error.Should().Contain("ChangeTheme",
			because: "the actionable message must point at the ChangeTheme feature as the likely cause");
	}

	[Test, Category("Unit")]
	[Description("Surfaces and augments the CanCustomizeBranding license hint when the UpdateQuery reports success=false with that error.")]
	public void SetUserTheme_ShouldMapLicenseError_WhenUpdateReportsCanCustomizeBranding() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		StubProfileSelect(ProfileId, "ocean-theme");
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Operation CanCustomizeBranding denied\"}}");
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "a success=false update must fail the command");
		error.Should().Contain("CanCustomizeBranding", because: "the license gate must be named in the diagnostic");
	}

	[Test, Category("Unit")]
	[Description("Surfaces and augments the CanChangeOwnTheme operation hint when the UpdateQuery reports success=false with that error.")]
	public void SetUserTheme_ShouldMapOperationError_WhenUpdateReportsCanChangeOwnTheme() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		StubProfileSelect(ProfileId, "ocean-theme");
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("UpdateQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"CanChangeOwnTheme is not granted\"}}");
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "a success=false update must fail the command");
		error.Should().Contain("CanChangeOwnTheme", because: "the operation gate must be named in the diagnostic");
	}

	[Test, Category("Unit")]
	[Description("Fails with an actionable message and posts no UpdateQuery when the current user's profile cannot be resolved (SysUserProfile returns no row).")]
	public void SetUserTheme_ShouldFail_WhenProfileHasNoRow() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		// The theme resolves, but the profile SelectQuery comes back with an empty row set.
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[]}");
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "the theme cannot be applied when the caller's profile id is unknown");
		error.Should().Contain("profile could not be resolved",
			because: "the message must tell the caller the profile lookup returned no row");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("UpdateQuery")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("Surfaces the list-themes service failure and posts no UpdateQuery when the theme catalog cannot be read.")]
	public void SetUserTheme_ShouldFail_WhenListThemesServiceFails() {
		// Arrange
		// GetAvailableThemes reports an explicit failure, so theme resolution cannot proceed.
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("GetAvailableThemes")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"catalog unavailable\"}}");
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "a theme cannot be resolved when the catalog read fails");
		error.Should().Contain("catalog unavailable",
			because: "the server-provided failure message must reach the caller instead of a generic error");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("UpdateQuery")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test, Category("Unit")]
	[Description("The options class declares the theming Creatio version floor so the command is gated to 10.0.0+ like the rest of the theming surface.")]
	public void SetUserThemeOptions_ShouldDeclareCreatioVersionFloor() {
		// Arrange
		// Act
		RequiresCreatioVersionAttribute attribute = typeof(SetUserThemeOptions)
			.GetCustomAttribute<RequiresCreatioVersionAttribute>();

		// Assert
		attribute.Should().NotBeNull(because: "the theming surface is only available on Creatio 10.0.0+");
		attribute.MinVersion.Should().Be(ThemeServiceRequirement.MinVersion,
			because: "set-user-theme shares the theming service version floor");
	}

	[Test, Category("Unit")]
	[Description("The profile id read and the verification read-back carry the configured MaxAttempts/RetryDelay into the SelectQuery calls, so a transient DataService verification blip is retried instead of reported as a false failure after the write committed.")]
	public void SetUserTheme_ShouldCarryConfiguredRetryPolicyIntoProfileSelects_WhenApplying() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		StubProfileSelect(ProfileId, "ocean-id");
		StubUpdateSuccess();
		SetUserThemeOptions options = new() { Theme = "Ocean", MaxAttempts = 4, RetryDelay = 3 };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeTrue(because: $"the stubbed apply flow succeeds: {error}");
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(u => u.Contains("SelectQuery")), Arg.Any<string>(), Arg.Any<int>(), 4, 3);
		// The `Received` above asserts the SelectQuery reads passed maxAttempts=4/delaySec=3 (the configured
		// policy), matching how list/update already retry — a one-attempt read could report a false failure.
	}

	[Test, Category("Unit")]
	[Description("An unexpected (programming) exception raised during a DataService round-trip propagates to the established top-level handlers instead of being masked as a misleading transport/parse failure.")]
	public void SetUserTheme_ShouldPropagateUnexpectedException_WhenProfileReadThrowsProgrammingError() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new InvalidCastException("programming error"));
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		Action act = () => _command.TrySetUserTheme(options, out _, out _);

		// Assert
		act.Should().Throw<InvalidCastException>(
			because: "a programming error is not in the expected I/O/parse set, so it must reach the top-level handlers rather than be converted into a 'Failed to read the profile' message");
	}

	[Test, Category("Unit")]
	[Description("An expected transport failure during a DataService round-trip is caught and reported as a contextual, actionable failure naming the step that failed — not propagated.")]
	public void SetUserTheme_ShouldReturnContextualFailure_WhenProfileReadThrowsTransportError() {
		// Arrange
		StubAvailableThemes(OceanThemeJson);
		_applicationClient.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new TimeoutException("timed out"));
		SetUserThemeOptions options = new() { Theme = "Ocean" };

		// Act
		bool succeeded = _command.TrySetUserTheme(options, out _, out string error);

		// Assert
		succeeded.Should().BeFalse(because: "a transport timeout is an expected failure that the applier handles");
		error.Should().Contain("Failed to read the current user's profile",
			because: "an expected failure must carry the step context so the caller can act on it");
	}
}
