using System;
using System.Net.Http;
using ATF.Repository.Providers;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SchemaNamePrefixToolTests {

	/// <summary>
	/// Builds a <see cref="SysSettingsManager"/> whose only meaningful collaborator is the data provider.
	/// The read path exercised here (<c>GetSysSettingValueByCode</c>) resolves the value through the
	/// provider, so the remaining constructor dependencies are inert substitutes.
	/// </summary>
	private static SysSettingsManager BuildSysSettingsManager(IDataProvider dataProvider) =>
		new(BuildAuthenticatedClient(),
			Substitute.For<IServiceUrlBuilder>(),
			dataProvider,
			Substitute.For<IWorkingDirectoriesProvider>(),
			Substitute.For<IFileSystem>(),
			Substitute.For<System.IO.Abstractions.IFileSystem>(),
			Substitute.For<ILogger>());

	// These scenarios describe a reachable environment with accepted credentials, so the
	// authenticated DataService probe has to answer with a real envelope; a substituted client
	// returns null, and an empty body is deliberately no longer taken as proof of authentication.
	private static IApplicationClient BuildAuthenticatedClient() {
		const string acceptedCredentialsEnvelope = "{\"rows\":[],\"success\":true}";
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(acceptedCredentialsEnvelope);
		// ExecutePostRequest takes the timeout, attempt count and delay as optional parameters, so a
		// two-argument stub only matches calls that leave all three at their defaults. The bounded
		// authentication probe passes its own values, so without this second stub NSubstitute answers
		// it with null and the empty body is read as rejected credentials.
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<int>())
			.Returns(acceptedCredentialsEnvelope);
		return applicationClient;
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name so callers and tests share the same production identifier.")]
	public void GetSchemaNamePrefix_Should_Advertise_Stable_Tool_Name() {
		// Arrange & Act
		string toolName = SchemaNamePrefixTool.GetSchemaNamePrefixToolName;

		// Assert
		toolName.Should().Be("get-schema-name-prefix",
			because: "the MCP tool name must stay centralized on the production type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the active prefix from the environment when the environment is reachable and the setting is configured.")]
	public void GetSchemaNamePrefix_Should_Return_Prefix_From_Environment() {
		// Arrange
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("SchemaNamePrefix").Returns("Usr");
		SysSettingsManager manager = BuildSysSettingsManager(dataProvider);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "a reachable environment with a configured prefix should produce a success response");
		result.SchemaNamePrefix.Should().Be("Usr",
			because: "the tool should return the raw prefix value read from the environment setting");
		result.Error.Should().BeNull(
			because: "successful calls should not include an error payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns empty prefix when SchemaNamePrefix is not configured — callers must not add a prefix in that case.")]
	public void GetSchemaNamePrefix_Should_Return_Empty_When_Setting_Is_Not_Configured() {
		// Arrange
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("SchemaNamePrefix").Returns(string.Empty);
		SysSettingsManager manager = BuildSysSettingsManager(dataProvider);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "an unconfigured prefix is a valid state, not a failure");
		result.SchemaNamePrefix.Should().BeEmpty(
			because: "no prefix means the caller should use plain PascalCase codes without a prefix");
		result.Error.Should().BeNull(
			because: "successful empty-prefix calls should not include an error");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns success:false with a network error message when environment connectivity fails.")]
	public void GetSchemaNamePrefix_Should_Return_Error_On_Network_Failure() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new HttpRequestException("Connection refused."));
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("offline-env"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a network failure should produce a structured error response, not propagate the exception");
		result.SchemaNamePrefix.Should().BeEmpty(
			because: "error responses must not expose a partial or defaulted prefix");
		result.Error.Should().Be("Network error reading SchemaNamePrefix.",
			because: "the error message must be a safe category label that does not expose raw exception details");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns success:false with a generic error message when environment resolution fails (unknown env name).")]
	public void GetSchemaNamePrefix_Should_Return_Error_When_Environment_Is_Not_Registered() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'unknown' is not registered."));
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("unknown"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unknown environment name should produce a structured error, not propagate the exception");
		result.SchemaNamePrefix.Should().BeEmpty(
			because: "error responses must not expose a partial or defaulted prefix");
		result.Error.Should().Be("Failed to read SchemaNamePrefix.",
			because: "non-network, non-auth failures should use the generic safe category label");
	}

	[Test]
	[Category("Unit")]
	[Description("Strips surrounding quotes from the setting value so agents receive a clean prefix code without punctuation.")]
	public void GetSchemaNamePrefix_Should_Strip_Surrounding_Quotes_From_Prefix() {
		// Arrange
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("SchemaNamePrefix").Returns("\"Usr\"");
		SysSettingsManager manager = BuildSysSettingsManager(dataProvider);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue();
		result.SchemaNamePrefix.Should().Be("Usr",
			because: "surrounding double-quotes in the raw setting value must be stripped before returning the prefix");
	}

	[Test]
	[Category("Unit")]
	[Description("get-schema-name-prefix carries the classified category, cause, recovery action and correlation ID beside its unchanged legacy error text (issue #1329).")]
	public void GetSchemaNamePrefix_Should_Carry_The_Classified_Failure_Parts() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new HttpRequestException("Connection refused."));
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("offline-env"));

		// Assert
		result.Error.Should().Be("Network error reading SchemaNamePrefix.",
			because: "the legacy error text stays byte-identical");
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.Network,
			because: "the category is what an agent branches on");
		result.Cause.Should().Be("The environment could not be reached.",
			because: "the cause is a fixed local diagnostic");
		result.RecoveryAction.Should().NotBeNullOrWhiteSpace(
			because: "the envelope must name the operator's next step");
		result.CorrelationId.Should().NotBeNullOrWhiteSpace(
			because: "#1222 requires a correlation ID on a failure envelope");
	}

	[Test]
	[Category("Unit")]
	[Description("A TLS handshake failure arrives as an AuthenticationException; get-schema-name-prefix must agree with the shared classifier and call it a network failure, instead of sending the operator to repair a working login while the untrusted certificate stays untouched.")]
	public void GetSchemaNamePrefix_Should_Report_Network_For_A_Certificate_Failure() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new HttpRequestException(
				"The SSL connection could not be established",
				new System.Security.Authentication.AuthenticationException(
					"The remote certificate is invalid according to the validation procedure.")));
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(),
			Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("tls-env"));

		// Assert
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.Network,
			because: "the framework raises AuthenticationException for a TLS handshake too, and the tool's own catch arm used to call that a credential rejection");
		result.Error.Should().Be("Network error reading SchemaNamePrefix.",
			because: "the shared classifier's verdict is what reaches the caller now");
	}

	[Test]
	[Category("Unit")]
	[Description("An AggregateException - how the Creatio client surfaces a transport fault through Task.Result - is unwrapped, instead of matching no arm and falling to the generic label.")]
	public void GetSchemaNamePrefix_Should_Unwrap_An_Aggregate_Transport_Fault() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new AggregateException(new HttpRequestException("Connection refused.")));
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(),
			Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("offline-env"));

		// Assert
		result.Error.Should().Be("Network error reading SchemaNamePrefix.",
			because: "the wrapper is not the fault; the tool used to report the generic label for it");
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.Network,
			because: "an unwrapped transport fault is a network failure");
	}

	[Test]
	[Category("Unit")]
	[Description("An unresolvable environment keeps the deliberately generic error label - resolver text is never promoted into the headline field - but its actionable text is surfaced as the cause with a Configuration category.")]
	public void GetSchemaNamePrefix_Should_Not_Promote_Resolver_Text_But_Surface_It_As_The_Cause() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment 'ghost' is not registered."));
		SchemaNamePrefixTool tool = new(commandResolver, new OperationCorrelationIdProvider(),
			Substitute.For<ILogger>());

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("ghost"));

		// Assert
		result.Error.Should().Be("Failed to read SchemaNamePrefix.",
			because: "this tool deliberately refuses to promote a resolver message into its error field");
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.Configuration,
			because: "an unregistered environment is a configuration failure, not an unknown one");
		result.Cause.Should().Be("Environment 'ghost' is not registered.",
			because: "the actionable text is not lost - it moves to the cause, beside a recovery action");
		result.RecoveryAction.Should().Contain("list-environments",
			because: "the caller needs the next step, not 'retry'");
	}

	[Test]
	[Category("Unit")]
	[Description("PR #1373 review: DescribeError is an ALLOW-LIST, so a category this tool does not recognise falls back to the generic label instead of promoting its message into the headline error.")]
	public void DescribeError_Should_Fall_Back_To_The_Generic_Label_For_An_Unrecognised_Category() {
		// Arrange
		SysSettingFailure unrecognised = new("Some future category's own message.", "SomeFutureCategory",
			"cause", "recovery", "abc123");

		// Act
		string described = SchemaNamePrefixTool.DescribeError(unrecognised);

		// Assert
		described.Should().Be(SchemaNamePrefixTool.GenericReadFailure,
			because: "a deny-list made promotion the DEFAULT, so a category added later would silently start putting its own text in the headline - and Configuration, added in this same change, is the proof categories do get added");
	}

	[Test]
	[Category("Unit")]
	[Description("PR #1373 review: exactly the three categories whose message genuinely is a promotable diagnosis are promoted; every other declared category gets the generic label.")]
	public void DescribeError_Should_Promote_Only_The_Three_Diagnosis_Carrying_Categories() {
		// Arrange
		string[] promotable = [
			SysSettingErrorCategories.Authentication,
			SysSettingErrorCategories.Network,
			SysSettingErrorCategories.ProviderFailure,
		];
		string[] declared = [.. typeof(SysSettingErrorCategories)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue()!)];
		declared.Should().NotBeEmpty(
			because: "an empty reflected set would make the loop below assert nothing");

		// Act & Assert
		foreach (string category in declared) {
			string described = SchemaNamePrefixTool.DescribeError(
				new SysSettingFailure("the category's own message", category, "cause", "recovery", "id"));
			if (promotable.Contains(category)) {
				described.Should().Be("the category's own message",
					because: $"'{category}' carries a diagnosis worth putting in the headline");
			} else {
				described.Should().Be(SchemaNamePrefixTool.GenericReadFailure,
					because: $"'{category}' is clio's own state or resolver text, which issue #1333 says must not become the headline error");
			}
		}
	}
}
