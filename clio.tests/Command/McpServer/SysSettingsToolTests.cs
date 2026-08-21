using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SysSettingsToolTests {

	private static IToolCommandResolver BuildResolver(ISysSettingsManager manager, IFileSystem fileSystem = null) {
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), fileSystem ?? Substitute.For<IFileSystem>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return commandResolver;
	}

	private static IToolCommandResolver BuildResolverThatThrows(Exception ex) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw ex);
		return commandResolver;
	}

	#region get-sys-setting

	[Test]
	[Category("Unit")]
	[Description("The get-sys-setting tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void GetSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingGetTool.GetSysSettingToolName.Should().Be("get-sys-setting",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns the All-Users default value resolved through the typed model path when the requested code exists on the environment.")]
	public void GetSysSetting_Should_Return_AllUsers_Default_Value_From_Environment() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("MaxFileSize").Returns(("10485760", "Integer"));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "MaxFileSize"));

		result.Success.Should().BeTrue(
			because: "the manager resolved the requested code via the All-Users-only path, so the tool reports a structured success envelope");
		result.Code.Should().Be("MaxFileSize",
			because: "the response must echo the code that was requested for traceability");
		result.Value.Should().Be("10485760",
			because: "the tool surfaces the manager's All-Users default value verbatim for non-sensitive types");
		result.Error.Should().BeNull(
			because: "a successful read must not populate the error envelope");
		manager.Received(1).GetAllUsersDefaultWithType("MaxFileSize");
		manager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting masks SecureText values so the direct read path does not bypass the masking applied by list-sys-settings.")]
	public void GetSysSetting_Should_Mask_SecureText_Value() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UsrApiSecret")
			.Returns(("ENCRYPTED_CIPHERTEXT_BASE64", "SecureText"));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UsrApiSecret"));

		result.Success.Should().BeTrue(
			because: "the read itself succeeds — only the value surface needs to be sanitized");
		result.Value.Should().Be("***",
			because: "SecureText values must be masked on every MCP read path or the masking policy on list-sys-settings is trivially bypassable");
		result.Error.Should().BeNull(
			because: "masking is a transparent presentation rule, not an error condition");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns an empty value for SecureText settings that have no All-Users row, distinguishing 'has secret' from 'no secret' without leaking either way.")]
	public void GetSysSetting_Should_Return_Empty_For_Unconfigured_SecureText() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UsrEmptySecret").Returns((string.Empty, "SecureText"));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UsrEmptySecret"));

		result.Value.Should().BeEmpty(
			because: "an unconfigured SecureText must surface as empty, not as the mask placeholder, so callers can tell whether a secret is present");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns an empty value (without surfacing an error) when the All-Users-only manager path reports no configured value.")]
	public void GetSysSetting_Should_Return_Empty_When_Setting_Is_Not_Configured() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UnknownCode").Returns((string.Empty, (string)null));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UnknownCode"));

		result.Success.Should().BeTrue(
			because: "an unknown setting is not a tool-level failure — the platform reports it as 'no value configured'");
		result.Value.Should().BeEmpty(
			because: "the tool must surface the absence of a value as an empty string, not synthesize a placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting short-circuits with a validation failure when the caller omits the required code argument.")]
	public void GetSysSetting_Should_Fail_When_Code_Is_Missing() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", ""));

		result.Success.Should().BeFalse(
			because: "a request without a code must not be forwarded to the platform");
		result.Error.Should().Contain("code is required",
			because: "the structured error must point the caller at the missing argument");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting maps HttpRequestException raised during environment resolution to a 'Network error' diagnostic message.")]
	public void GetSysSetting_Should_Categorize_Network_Errors() {
		SysSettingGetTool tool = new(BuildResolverThatThrows(new HttpRequestException("Connection refused.")));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("offline", "MaxFileSize"));

		result.Success.Should().BeFalse(
			because: "an unreachable environment must surface a failure envelope, not propagate the raw exception");
		result.Value.Should().BeEmpty(
			because: "no value could be resolved, so the envelope must not synthesize one");
		result.Error.Should().Be("Network error reading sys-setting.",
			because: "the CategorizeError fallback maps HttpRequestException to the canonical 'Network error' message");
	}

	#endregion

	#region list-sys-settings

	[Test]
	[Category("Unit")]
	[Description("The list-sys-settings tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void ListSysSettings_Should_Advertise_Stable_Tool_Name() {
		SysSettingsListTool.ListSysSettingsToolName.Should().Be("list-sys-settings",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("list-sys-settings maps unexpected exceptions raised during resolution to a generic 'Failed listing' diagnostic.")]
	public void ListSysSettings_Should_Categorize_Generic_Failures() {
		SysSettingsListTool tool = new(BuildResolverThatThrows(new TimeoutException("read timed out")));

		SysSettingsListResult result = tool.ListSysSettings(new ListSysSettingsArgs("local"));

		result.Success.Should().BeFalse(
			because: "an unexpected exception during resolve must surface as a structured failure rather than an unhandled exception");
		result.Settings.Should().BeEmpty(
			because: "no rows were produced, so the envelope must not synthesize a partial list");
		result.Error.Should().Be("Failed listing sys-settings.",
			because: "the CategorizeError fallback maps non-network/auth exceptions to the canonical 'Failed listing' message");
	}

	#endregion

	#region create-sys-setting

	[Test]
	[Category("Unit")]
	[Description("The create-sys-setting tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void CreateSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingCreateTool.CreateSysSettingToolName.Should().Be("create-sys-setting",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting short-circuits with a validation failure when a required field (code) is empty.")]
	public void CreateSysSetting_Should_Reject_Missing_Required_Fields() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "", "Display", "Integer"));

		result.Success.Should().BeFalse(
			because: "an empty code is invalid input and must short-circuit before the platform is contacted");
		result.Error.Should().Contain("code is required",
			because: "the structured error must point the caller at the missing argument");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting refuses unknown value-type-names to keep the tool surface aligned with Creatio's internal type registry.")]
	public void CreateSysSetting_Should_Reject_Unsupported_Value_Type() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "MyCode", "MyName", "UnsupportedType"));

		result.Success.Should().BeFalse(
			because: "the tool must reject value-type-names that are not in the Creatio internal registry");
		result.Error.Should().Contain("Unsupported value-type-name",
			because: "the structured error must explain why the value-type-name was refused");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting accepts Binary value-type-name: binary sys-settings (logos/images) are a supported write type provisioned like any other value-type-name.")]
	public void CreateSysSetting_Should_Accept_Binary_Value_Type() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.InsertSysSetting(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>())
			.Returns(new SysSettingsManager.InsertSysSettingResponse(
				new SysSettingsManager.ResponseStatus(string.Empty, string.Empty, Array.Empty<object>()),
				Guid.NewGuid(), 1, false, true));
		SysSettingCreateTool tool = new(BuildResolver(manager));

		// Act
		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "UsrBinCode", "Binary setting", "Binary"));

		// Assert
		result.Success.Should().BeTrue(
			because: "Binary is a supported write type now, so create must provision it like any other value-type-name");
		result.ValueTypeName.Should().Be("Binary",
			because: "the created setting echoes the Binary value-type-name it was created with");
		manager.Received(1).InsertSysSetting("Binary setting", "UsrBinCode", "Binary",
			Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>());
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting maps UnauthorizedAccessException raised during environment resolution to an 'Authentication error' diagnostic.")]
	public void CreateSysSetting_Should_Categorize_Authentication_Errors() {
		SysSettingCreateTool tool = new(BuildResolverThatThrows(new UnauthorizedAccessException("Forbidden")));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "MyCode", "MyName", "Text"));

		result.Success.Should().BeFalse(
			because: "the resolver failed authentication and the tool must surface a structured failure envelope");
		result.Error.Should().Be("Authentication error creating sys-setting.",
			because: "the CategorizeError fallback maps UnauthorizedAccessException to the canonical 'Authentication error' message");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting masks the readback value for SecureText so the structured response does not echo the freshly-written secret back to the caller in clear.")]
	public void CreateSysSetting_Should_Mask_SecureText_Readback() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.InsertSysSetting(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>())
			.Returns(new SysSettingsManager.InsertSysSettingResponse(
				new SysSettingsManager.ResponseStatus(string.Empty, string.Empty, Array.Empty<object>()),
				Guid.NewGuid(), 1, false, true));
		manager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		manager.GetAllUsersDefaultByCode("UsrApiSecret").Returns("ENCRYPTED_CIPHERTEXT_BASE64");
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "UsrApiSecret", "API secret", "SecureText", Value: "plaintext"));

		result.Success.Should().BeTrue(
			because: "the create itself succeeds — only the readback value surface needs to be sanitized");
		result.Value.Should().Be("***",
			because: "create-sys-setting must not echo the freshly-stored SecureText back in clear or it bypasses the masking policy");
	}

	#endregion

	#region update-sys-setting

	[Test]
	[Category("Unit")]
	[Description("The update-sys-setting tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void UpdateSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingUpdateTool.UpdateSysSettingToolName.Should().Be("update-sys-setting",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting short-circuits with a validation failure when the caller omits the required code argument.")]
	public void UpdateSysSetting_Should_Reject_Missing_Code() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "", "x"));

		result.Success.Should().BeFalse(
			because: "an empty code is invalid input and must short-circuit before the platform is contacted");
		result.Error.Should().Contain("code is required",
			because: "the structured error must point the caller at the missing argument");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting maps HttpRequestException raised during environment resolution to a 'Network error' diagnostic message.")]
	public void UpdateSysSetting_Should_Categorize_Network_Errors() {
		SysSettingUpdateTool tool = new(BuildResolverThatThrows(new HttpRequestException("Connection refused.")));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("offline", "MaxFileSize", "10485760"));

		result.Success.Should().BeFalse(
			because: "an unreachable environment must surface a failure envelope, not propagate the raw exception");
		result.Error.Should().Be("Network error updating sys-setting.",
			because: "the CategorizeError fallback maps HttpRequestException to the canonical 'Network error' message");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting masks the readback value for SecureText so the response does not echo the just-written secret back to the caller in clear.")]
	public void UpdateSysSetting_Should_Mask_SecureText_Readback() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.UpdateSysSetting("UsrApiSecret", Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		manager.GetAllUsersDefaultWithType("UsrApiSecret")
			.Returns(("ENCRYPTED_CIPHERTEXT_BASE64", "SecureText"));
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "UsrApiSecret", "plaintext", "SecureText"));

		result.Success.Should().BeTrue(
			because: "the update itself succeeds — only the readback value surface needs to be sanitized");
		result.Value.Should().Be("***",
			because: "update-sys-setting must not echo the freshly-stored SecureText back in clear or it bypasses the masking policy");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting reads the file at value-file-path, Base64-encodes it locally, and writes it as the value so image/logo blobs never travel through the tool arguments.")]
	public void UpdateSysSetting_Should_Encode_File_From_ValueFilePath() {
		// Arrange
		byte[] bytes = [1, 2, 3, 4, 5];
		string expectedBase64 = Convert.ToBase64String(bytes);
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		string capturedValue = null;
		manager.UpdateSysSetting("LogoImage", Arg.Do<object>(v => capturedValue = v as string), Arg.Any<string>())
			.Returns(true);
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(FileSecurityPolicy.DisabledPolicy);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("logo.png").Returns(true);
		fileSystem.OpenReadStream("logo.png").Returns(_ => new MemoryStream(bytes));
		SysSettingUpdateTool tool = new(BuildResolver(manager, fileSystem));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "LogoImage", ValueFilePath: "logo.png"));

		// Assert
		result.Success.Should().BeTrue(
			because: "a readable file path must produce a successful binary write");
		capturedValue.Should().Be(expectedBase64,
			because: "clio must Base64-encode the file bytes locally before sending them to the platform");
		manager.Received(1).UpdateSysSetting("LogoImage", expectedBase64, Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting rejects a call that supplies both value and value-file-path because the payload source would be ambiguous.")]
	public void UpdateSysSetting_Should_Reject_Both_Value_And_FilePath() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "LogoImage", "rawbase64", ValueFilePath: "logo.png"));

		// Assert
		result.Success.Should().BeFalse(
			because: "value and value-file-path are mutually exclusive sources for the payload");
		result.Error.Should().Contain("not both",
			because: "the error must tell the caller to pass only one payload source");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting rejects a value-file-path upload when the existing target setting is not Binary, so a file's Base64 is never persisted as text.")]
	public void UpdateSysSetting_Should_Reject_File_For_NonBinary_Setting() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("SchemaNamePrefix").Returns(("Usr", "Text"));
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("logo.png").Returns(true);
		SysSettingUpdateTool tool = new(BuildResolver(manager, fileSystem));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "SchemaNamePrefix", ValueFilePath: "logo.png"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a file can only be uploaded to a Binary setting, not a Text one");
		result.Error.Should().Contain("not Binary",
			because: "the error must explain the target setting's type is wrong for a file upload");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
		fileSystem.DidNotReceive().OpenReadStream(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting rejects a value-file-path upload when the target setting does not exist, directing the caller to create it as Binary first.")]
	public void UpdateSysSetting_Should_Reject_File_For_Missing_Setting() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UsrNope").Returns((string.Empty, (string)null));
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("logo.png").Returns(true);
		SysSettingUpdateTool tool = new(BuildResolver(manager, fileSystem));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "UsrNope", ValueFilePath: "logo.png"));

		// Assert
		result.Success.Should().BeFalse(
			because: "update requires the setting to already exist");
		result.Error.Should().Contain("was not found",
			because: "the error must tell the caller to create the Binary setting first");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting rejects a value-file-path upload whose extension is blocked by the environment's active file-security policy, mirroring the platform's upload rules.")]
	public void UpdateSysSetting_Should_Reject_File_Blocked_By_Security_Policy() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(new FileSecurityPolicy(
			FileSecurityMode.DenyList, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "exe", "svg" }, true));
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("payload.svg").Returns(true);
		SysSettingUpdateTool tool = new(BuildResolver(manager, fileSystem));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "LogoImage", ValueFilePath: "payload.svg"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a denied extension must be rejected before the file is read or uploaded");
		result.Error.Should().Contain(".svg",
			because: "the error must name the blocked extension so the caller understands the policy");
		fileSystem.DidNotReceive().OpenReadStream(Arg.Any<string>());
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting rejects an inline Base64 value for a Binary setting while a file-security policy is active, since an inline value carries no extension to validate.")]
	public void UpdateSysSetting_Should_Reject_Inline_Binary_Under_Active_Policy() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(new FileSecurityPolicy(
			FileSecurityMode.AllowList, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "png" }, false));
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "LogoImage", "aVZCT1J3"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an inline Binary value has no extension to validate against an active policy");
		result.Error.Should().Contain("value-file-path",
			because: "the error must direct the caller to the file-path form so the extension can be checked");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting refuses a file upload when the environment's file-security mode cannot be resolved (fails closed), since clio is the only policy barrier on this write path.")]
	public void UpdateSysSetting_Should_Refuse_File_When_Mode_Unknown() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(FileSecurityPolicy.UnknownPolicy);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("logo.png").Returns(true);
		SysSettingUpdateTool tool = new(BuildResolver(manager, fileSystem));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "LogoImage", ValueFilePath: "logo.png"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unresolvable file-security mode must fail closed, not upload");
		result.Error.Should().Contain("Cannot determine",
			because: "the error must state the mode could not be determined");
		fileSystem.DidNotReceive().OpenReadStream(Arg.Any<string>());
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting surfaces the specific Binary validation cause (from the manager) to the MCP result instead of a generic update-failure message.")]
	public void UpdateSysSetting_Inline_Binary_Should_Surface_Specific_Validation_Error() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(FileSecurityPolicy.DisabledPolicy);
		manager.TryValidateBinaryValue(Arg.Any<string>(), out Arg.Any<string>())
			.Returns(call => { call[1] = "Binary value exceeds the 10,485,760-byte limit."; return false; });
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		// Act
		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "LogoImage", "QUJD"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an invalid inline Binary value must fail");
		result.Error.Should().Contain("exceeds",
			because: "the specific validation cause must reach the MCP result, not the generic update-failure message");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	#endregion

	#region set-syssetting CLI (Binary file path)

	[Test]
	[Category("Unit")]
	[Description("The set-syssetting CLI reads a Binary value from a file path on disk and sends it Base64-encoded, keeping the logo/image bytes off the command line.")]
	public void Cli_SetSysSetting_Binary_Should_Encode_File() {
		// Arrange
		byte[] bytes = [10, 20, 30, 40];
		string expectedBase64 = Convert.ToBase64String(bytes);
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		string capturedValue = null;
		manager.UpdateSysSetting("LogoImage", Arg.Do<object>(v => capturedValue = v as string), "Binary")
			.Returns(true);
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(FileSecurityPolicy.DisabledPolicy);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("logo.png").Returns(true);
		fileSystem.OpenReadStream("logo.png").Returns(_ => new MemoryStream(bytes));
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), fileSystem);

		// Act
		command.UpdateSysSetting(new SysSettingsOptions { Code = "LogoImage", Value = "logo.png", Type = "Binary" });

		// Assert
		capturedValue.Should().Be(expectedBase64,
			because: "the CLI must read the file and Base64-encode its bytes before handing them to the manager");
		manager.Received(1).UpdateSysSetting("LogoImage", expectedBase64, "Binary");
	}

	[Test]
	[Category("Unit")]
	[Description("The set-syssetting CLI reports a clear 'file not found' error when a Binary value looks like a path but no such file exists, instead of failing later as invalid Base64.")]
	public void Cli_SetSysSetting_Binary_MissingFile_Should_Report_FileNotFound() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), fileSystem);

		// Act
		Action act = () => command.UpdateSysSetting(
			new SysSettingsOptions { Code = "LogoImage", Value = @"C:\missing\logo.png", Type = "Binary" });

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*File not found*",
				because: "a path-like Binary value with no matching file must fail with a file-not-found message, not an invalid-Base64 one");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("The set-syssetting CLI rejects a Binary file whose content exceeds the size cap during the bounded read, so an oversized (or mid-read grown) file cannot be uploaded.")]
	public void Cli_SetSysSetting_Binary_OverCap_Should_Reject_During_Read() {
		// Arrange — a stream that yields more than the cap, simulating an oversized/grown file.
		long cap = SysSettingsManager.MaxBinaryValueBytes;
		Stream oversized = new MemoryStream(new byte[cap + 1]);
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(FileSecurityPolicy.DisabledPolicy);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("big.png").Returns(true);
		fileSystem.OpenReadStream("big.png").Returns(oversized);
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), fileSystem);

		// Act
		Action act = () => command.UpdateSysSetting(
			new SysSettingsOptions { Code = "LogoImage", Value = "big.png", Type = "Binary" });

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*limit*",
				because: "the bounded read must reject content exceeding the cap instead of allocating it all and uploading");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("The set-syssetting CLI rejects an inline Base64 Binary value while a file-security policy is active, matching the MCP path so the CLI cannot bypass the policy.")]
	public void Cli_SetSysSetting_Inline_Binary_Under_Active_Policy_Should_Reject() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("LogoImage").Returns((string.Empty, "Binary"));
		manager.GetFileSecurityPolicy().Returns(new FileSecurityPolicy(
			FileSecurityMode.DenyList, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "exe" }, true));
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile("QUJD").Returns(false); // an inline Base64 value, not a file
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(), fileSystem);

		// Act
		Action act = () => command.UpdateSysSetting(
			new SysSettingsOptions { Code = "LogoImage", Value = "QUJD", Type = "Binary" });

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*active file-security policy*",
				because: "inline Binary must be refused under an active policy on the CLI too, not just MCP");
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	#endregion

	#region create-sys-setting (Binary under active policy)

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting refuses a Binary initial value while a file-security policy is active, before creating anything, so the create path cannot bypass the policy.")]
	public void CreateSysSetting_Binary_Initial_Value_Under_Active_Policy_Should_Reject() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetFileSecurityPolicy().Returns(new FileSecurityPolicy(
			FileSecurityMode.DenyList, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "exe" }, true));
		SysSettingCreateTool tool = new(BuildResolver(manager));

		// Act
		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "UsrBinCode", "Binary setting", "Binary", Value: "QUJD"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a Binary initial value is inline and must be refused under an active policy");
		result.Error.Should().Contain("active file-security policy",
			because: "the error must explain why the inline Binary create value was refused");
		manager.DidNotReceive().InsertSysSetting(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>());
	}

	#endregion
}
