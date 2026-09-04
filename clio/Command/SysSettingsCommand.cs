using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using CommandLine;
using CreatioModel;

namespace Clio.Command
{
	[Verb("set-syssetting", Aliases =  ["ss", "syssetting", "sys-setting", "get-syssetting"], HelpText = "Set setting value")]
	public class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Sys-setting code")]
		public string Code { get; set; }

		[Value(1, MetaName = "Value", Required = false, HelpText =
			"Sys-setting value. When Type is Binary (a setting whose value is stored as blob data, such as the " +
			"logo), pass the path to a file and clio uploads its contents for you.")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText =
			"Sys-setting type (default: Text). Use Binary for a setting whose value is blob data, such as the logo.", Default = "Text")]
		public string Type { get; set; }

		[Option("get", Required = false, HelpText = "Use GET to retrieve sys-setting")]
		public bool IsGet { get; set; }

		[Option("GET", Required = false, Hidden = true, HelpText = "Alias for --get")]
		public bool IsGetAlias {
			get => IsGet;
			set { if (value) IsGet = value; }
		}

	}

	public class SysSettingsCommand : Command<SysSettingsOptions> {

		private const string LookupTypeName = "Lookup";
		private const string BinaryTypeName = "Binary";

		// Binary sys-settings (a value stored as blob data, e.g. the logo) are supported for WRITE: the value is a Base64 payload
		// sent through PostSysSettingsValues, exactly like every other type. Prefer supplying a file
		// path (CLI value / MCP value-file-path) so clio reads and encodes the blob locally instead of
		// pushing a large Base64 string through the tool-call arguments. The MCP read surface does not
		// return a Binary value — clio's SysSettingsValue model maps no binary column, so get-sys-setting
		// returns empty and list-sys-settings shows "<binary>". The raw Base64 is still available via the
		// legacy CLI "get-syssetting <code>", which reads it through the cliogate endpoint.
		private static readonly string[] SupportedValueTypeNames = [
			"Text", "ShortText", "MediumText", "LongText", "SecureText", "MaxSizeText",
			"Boolean", "DateTime", "Date", "Time", "Integer",
			"Money", "Float", LookupTypeName,
			"Currency", "Decimal", BinaryTypeName
		];

		private readonly ISysSettingsManager _sysSettingsManager;
		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;
		private readonly IOperationCorrelationIdProvider _correlationIds;

		public SysSettingsCommand(ISysSettingsManager sysSettingsManager, ILogger logger, IFileSystem fileSystem,
			IOperationCorrelationIdProvider correlationIds){
			_sysSettingsManager = sysSettingsManager;
			_logger = logger;
			_fileSystem = fileSystem;
			_correlationIds = correlationIds;
		}

		/// <summary>
		/// Reads the file at <paramref name="filePath"/> and returns its Base64-encoded contents.
		/// Used to turn a file's bytes (e.g. the logo, or any blob) into the Base64 payload a Binary
		/// sys-setting expects, keeping the bytes on disk rather than in the CLI/MCP arguments. Reads from a
		/// single open handle and stops as soon as the content exceeds
		/// <see cref="SysSettingsManager.MaxBinaryValueBytes"/>, so a file that grows or is replaced after
		/// any metadata inspection cannot force an unbounded allocation. The manager re-checks the decoded
		/// length, so the limit also holds for inline Base64.
		/// </summary>
		private string EncodeFileToBase64(string filePath){
			if (!_fileSystem.ExistsFile(filePath)) {
				throw new ArgumentException($"File not found: '{filePath}'.");
			}
			long cap = SysSettingsManager.MaxBinaryValueBytes;
			using Stream stream = _fileSystem.OpenReadStream(filePath);
			using MemoryStream buffered = new();
			byte[] chunk = new byte[81920];
			int read;
			while ((read = stream.Read(chunk, 0, chunk.Length)) > 0) {
				if (buffered.Length + read > cap) {
					throw new ArgumentException(
						$"File '{filePath}' exceeds the {cap:N0}-byte limit for a Binary sys-setting value.");
				}
				buffered.Write(chunk, 0, read);
			}
			byte[] bytes = buffered.ToArray();
			_logger.WriteInfo($"Reading Binary sys-setting value from file '{filePath}' ({bytes.LongLength:N0} bytes).");
			return Convert.ToBase64String(bytes);
		}

		/// <summary>
		/// Confirms the existing sys-setting <paramref name="code"/> is Binary before a file is uploaded to
		/// it. Prevents a file's Base64 from being persisted as text on a non-Binary setting, and never lets
		/// a caller-supplied value-type-name override the actual type of an existing setting.
		/// </summary>
		private void EnsureExistingSettingIsBinary(string code){
			(_, string existingType) = _sysSettingsManager.GetAllUsersDefaultWithType(code);
			if (existingType is null) {
				throw new ArgumentException(DescribeUnreadableBinaryTarget(code));
			}
			if (!string.Equals(existingType, BinaryTypeName, StringComparison.Ordinal)) {
				throw new ArgumentException(
					$"Cannot upload a file to sys-setting '{code}': it is type '{existingType}', not Binary. " +
					"A file value can only be written to a Binary setting.");
			}
		}

		/// <summary>
		/// Applies the environment's active file-security policy to <paramref name="filePath"/> before upload,
		/// mirroring how Creatio would treat the same file on its upload service (extension allow/deny +
		/// unknown-type). Advisory client-side check: the platform does not gate the sys-setting write path
		/// itself, but this keeps a Binary upload consistent with the environment's configured policy.
		/// </summary>
		private void EnforceFileSecurityPolicy(string filePath){
			FileSecurityPolicy policy = _sysSettingsManager.GetFileSecurityPolicy();
			if (!policy.IsActive) {
				return;
			}
			// Fail closed: if the environment's file-security mode could not be resolved, refuse rather than
			// upload — this client-side check is the only policy barrier on the sys-setting write path.
			if (policy.Mode == FileSecurityMode.Unknown) {
				throw new ArgumentException(
					"Cannot determine the environment file-security mode; Binary upload was refused.");
			}
			string fileName = Path.GetFileName(filePath);
			string extension = Path.GetExtension(filePath).TrimStart('.');
			if (string.IsNullOrEmpty(extension)) {
				if (!policy.AllowUnknownType) {
					throw new ArgumentException(
						$"Cannot upload '{fileName}': files with no extension are not allowed in this environment " +
						"(AllowFilesWithUnknownType is off).");
				}
				return;
			}
			bool listed = policy.Extensions.Contains(extension);
			bool allowed = policy.Mode == FileSecurityMode.AllowList ? listed : !listed;
			if (!allowed) {
				throw new ArgumentException(
					$"Cannot upload '{fileName}': files with extension '.{extension}' are not allowed in this " +
					$"environment ({policy.Mode} file-security policy).");
			}
		}

		/// <summary>
		/// Rejects an inline Base64 value for a Binary setting while a file-security policy is active: an
		/// inline value carries no filename/extension, so it would bypass the environment's extension policy.
		/// The caller must use value-file-path (which has an extension to validate) instead.
		/// </summary>
		private void RejectInlineBinaryUnderActivePolicy(string code){
			(_, string existingType) = _sysSettingsManager.GetAllUsersDefaultWithType(code);
			RejectInlineBinaryUnderActivePolicy(code,
				string.Equals(existingType, BinaryTypeName, StringComparison.Ordinal));
		}

		/// <summary>
		/// Rejects an inline Base64 value for a Binary setting while a file-security policy is active (an
		/// inline value has no extension to validate). Overload takes the known target type so callers that
		/// already know it (e.g. create-sys-setting) need not resolve it again.
		/// </summary>
		private void RejectInlineBinaryUnderActivePolicy(string code, bool targetIsBinary){
			if (!targetIsBinary) {
				return;
			}
			FileSecurityPolicy policy = _sysSettingsManager.GetFileSecurityPolicy();
			if (policy.Mode == FileSecurityMode.Unknown) {
				throw new ArgumentException(
					"Cannot determine the environment file-security mode; Binary upload was refused.");
			}
			if (policy.IsActive) {
				throw new ArgumentException(
					$"Sys-setting '{code}' is Binary and this environment has an active file-security policy. " +
					"Provide the value via value-file-path (a file path) so its extension can be validated, " +
					"rather than an inline Base64 value.");
			}
		}

		// A Base64 string uses only [A-Za-z0-9+/=], so any of '.', '\' or ':' means the caller almost
		// certainly meant a file path — used to give a "file not found" hint instead of a Base64 error.
		private static bool LooksLikeFilePath(string value) =>
			value.IndexOf('.') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0;

		private void CreateSysSettingIfNotExists(SysSettingsOptions opts) {
			_sysSettingsManager.CreateSysSettingIfNotExists(opts.Code, opts.Code, opts.Type);
		}

		/// <summary>
		/// Writes the sys-setting value named by <paramref name="opts"/>, reading and Base64-encoding a Binary
		/// value from disk when the value points at a file.
		/// </summary>
		/// <param name="opts">The setting code, value and value-type-name.</param>
		/// <param name="settings">Unused; kept for the call shape the other command methods share.</param>
		/// <returns><see langword="false"/> when the environment did not apply the value.</returns>
		public bool UpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			// For a Binary setting, a value that points at an existing file is read and Base64-encoded
			// locally (the blob upload path, e.g. the logo); an inline Base64 string is passed through as-is.
			string value = opts.Value;
			if (string.Equals(opts.Type, BinaryTypeName, StringComparison.Ordinal) && opts.Value is not null) {
				if (_fileSystem.ExistsFile(opts.Value)) {
					EnsureExistingSettingIsBinary(opts.Code);
					EnforceFileSecurityPolicy(opts.Value);
					value = EncodeFileToBase64(opts.Value);
				} else if (LooksLikeFilePath(opts.Value)) {
					// The value looks like a path (Base64 never contains '.', '\\' or ':') but no such file
					// exists — report that plainly instead of letting it fail later as "invalid Base64".
					throw new ArgumentException(
						$"File not found: '{opts.Value}'. For a Binary setting pass a path to an existing " +
						"file (e.g. the logo), or a Base64 string.");
				} else {
					// Inline Base64 for a Binary setting: subject to the same file-security gate as the MCP path.
					RejectInlineBinaryUnderActivePolicy(opts.Code);
				}
			}
			bool isUpdated = _sysSettingsManager.UpdateSysSetting(opts.Code, value, opts.Type);
			if(isUpdated) {
				_logger.WriteInfo($"SysSettings with code: {opts.Code} updated.");
			} else {
				_logger.WriteError($"SysSettings with code: {opts.Code} is not updated.");
			}
			return isUpdated;
		}

		public void TryUpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			try {
				UpdateSysSetting(opts, settings);
			} catch (Exception ex) {
				//Routed through the shared classifier rather than collapsed to "is not updated": this
				//bare catch used to swallow the credential and network diagnoses that the typed
				//TryUpdateSysSetting(UpdateSysSettingArgs) overload reports, so the CLI path told the
				//operator nothing about WHY the write did not land.
				//Goes through the same report path as every other failure: the CLI overload used to mint
				//an ID and write its own line, which meant the debug-verbosity server excerpt was never
				//written for the one path an operator actually runs interactively
				//(apply-environment-manifest, Program.cs).
				SysSettingFailure failure = CategorizeAndLog(ex, UpdateOperationLabel, _logger,
					_correlationIds);
				//PR #1373 review: the local IS used. This second line is what
				//docs/knowledge/Command/refused-syssetting-update-is-only-visible-as-a-writeerror.md pins as the
				//Maintainer / apply-environment-manifest flow's ONLY failure signal, and after the classifier was
				//added in front of it the line carried no diagnosis at all - so the one line an operator or a
				//parser reads pointed at nothing. It now carries the correlation ID of the classified record above,
				//which is the bridge between the two lines; exactly one ID is minted per failure.
				_logger.WriteError(
					$"SysSettings with code: {opts.Code} is not updated. (correlation-id: {failure.CorrelationId})");
			}
		}

		/// <summary>
		/// Updates an existing sys-setting value. The provided <c>value-type-name</c> is used only as a
		/// fallback when the setting type cannot be resolved on the target environment.
		/// </summary>
		public SysSettingUpdateResult TryUpdateSysSetting(UpdateSysSettingArgs args) {
			try {
				bool hasFilePath = ValidateUpdateArgs(args);
				string value = PrepareUpdateValue(args, hasFilePath, out string valueTypeName);
				bool updated = _sysSettingsManager.UpdateSysSetting(args.Code, value, valueTypeName);
				if (!updated) {
					//PR #1373 review: same as the create refusal - a non-exception `false` is a real failure and
					//must carry the four envelope fields rather than the all-null shape the contract reads as
					//success.
					SysSettingFailure refusal = ReportRefusal(UpdateOperationLabel,
						SysSettingErrorCategories.ProviderFailure, SysSettingFailureTexts.RefusedUpdateCause,
						SysSettingFailureTexts.RefusedUpdateRecovery);
					return new SysSettingUpdateResult(false, args.Code, null,
						"Failed to update sys-setting. The setting may not exist, or the value did not match the expected type.",
						refusal.Category, refusal.Cause, refusal.RecoveryAction, refusal.CorrelationId);
				}
				(string readback, string readbackType) = _sysSettingsManager.GetAllUsersDefaultWithType(args.Code);
				return new SysSettingUpdateResult(true, args.Code, ApplySecureTextMask(readbackType, readback));
			} catch (Exception ex) {
				SysSettingFailure failure = ReportFailure(ex, UpdateOperationLabel);
				return new SysSettingUpdateResult(false, args.Code, null, failure.Error,
					failure.Category, failure.Cause, failure.RecoveryAction, failure.CorrelationId);
			}
		}

		/// <summary>
		/// Validates the update arguments: a non-empty code and exactly one of <c>value</c> / <c>value-file-path</c>.
		/// Returns whether the payload comes from a file path. Throws <see cref="ArgumentException"/> on invalid input.
		/// </summary>
		private static bool ValidateUpdateArgs(UpdateSysSettingArgs args){
			if (string.IsNullOrWhiteSpace(args.Code)) {
				throw new ArgumentException("code is required.");
			}
			bool hasInlineValue = args.Value is not null;
			bool hasFilePath = !string.IsNullOrWhiteSpace(args.ValueFilePath);
			if (hasInlineValue && hasFilePath) {
				throw new ArgumentException("Provide either 'value' or 'value-file-path', not both.");
			}
			if (!hasInlineValue && !hasFilePath) {
				throw new ArgumentException("value is required (supply 'value' or 'value-file-path').");
			}
			return hasFilePath;
		}

		/// <summary>
		/// Produces the value to send and resolves the value-type-name, applying the Binary write guards:
		/// a file upload requires an existing Binary target that passes the file-security policy; an inline
		/// value for a Binary target is refused under an active policy and otherwise validated up front (the
		/// specific malformed/too-large cause is thrown so it surfaces on the result). Throws on any violation.
		/// </summary>
		private string PrepareUpdateValue(UpdateSysSettingArgs args, bool hasFilePath, out string valueTypeName){
			// Resolve the existing type once so file/inline paths share it (avoids a second lookup).
			(_, string existingType) = _sysSettingsManager.GetAllUsersDefaultWithType(args.Code);
			bool targetIsBinary = string.Equals(existingType, BinaryTypeName, StringComparison.Ordinal);
			string value;
			if (hasFilePath) {
				if (existingType is null) {
					throw new ArgumentException(DescribeUnreadableBinaryTarget(args.Code));
				}
				if (!targetIsBinary) {
					throw new ArgumentException(
						$"Cannot upload a file to sys-setting '{args.Code}': it is type '{existingType}', not Binary. " +
						"A file value can only be written to a Binary setting.");
				}
				EnforceFileSecurityPolicy(args.ValueFilePath);
				value = EncodeFileToBase64(args.ValueFilePath);
			} else {
				RejectInlineBinaryUnderActivePolicy(args.Code, targetIsBinary);
				value = args.Value;
				if (targetIsBinary && !_sysSettingsManager.TryValidateBinaryValue(value, out string binaryError)) {
					throw new ArgumentException(binaryError);
				}
			}
			// A file-derived payload is Binary by nature; default the type accordingly when it is not
			// resolved from the target environment.
			string fallbackTypeName = hasFilePath ? BinaryTypeName : "Text";
			valueTypeName = string.IsNullOrWhiteSpace(args.ValueTypeName) ? fallbackTypeName : args.ValueTypeName;
			return value;
		}

		public override int Execute(SysSettingsOptions opts) {
			if(opts.IsGet) {
				if(opts.Value is not null) {
					_logger.WriteWarning(
						$"A value was supplied but 'get-syssetting'/--get only reads; the value is ignored. " +
						$"Use 'clio set-syssetting {opts.Code} <value>' to write it.");
				}
				string value = _sysSettingsManager.GetSysSettingValueByCode(opts.Code);
				_logger.WriteInfo($"SysSettings {opts.Code} : {value}");
				return 0;
			}

			// A missing value must never overwrite an existing setting with an empty string.
			// Bail out instead of silently clearing the value (e.g. `set-syssetting <code>` with no
			// value, or a `get-syssetting` invocation that did not resolve to the read path).
			if(opts.Value is null) {
				_logger.WriteError(
					$"No value provided for sys-setting '{opts.Code}'. " +
					"Provide a value to set it (e.g. 'clio set-syssetting <code> <value>'), " +
					"or use 'clio get-syssetting <code>' / 'clio set-syssetting <code> --get' to read it.");
				return 1;
			}

			//WHICH step is running, tracked rather than assumed (PR #1374 review). The try below wraps
			//BOTH CreateSysSettingIfNotExists and UpdateSysSetting, and reporting every failure as
			//"updating sys-setting" pointed the operator at the wrong operation: an unsupported
			//value-type-name or an unresolvable reference-schema-name is an ArgumentException raised from
			//ValidateCreateArgs / ResolveReferenceSchemaUId, i.e. from the CREATE step.
			string operationLabel = CreateOperationLabel;
			try {
				CreateSysSettingIfNotExists(opts);
				operationLabel = UpdateOperationLabel;
				if (!UpdateSysSetting(opts)) {
					return 1;
				}
			} catch (Exception ex) when (CarriesServerText(ex)) {
				//Was `ex.Message` raw: on this path that message is composed by ClassifyingDataProvider or
				//the write-path guard, so the raw form could carry server prose straight to the console
				//(issue #1333) - and it named no cause and no recovery action either.
				_logger.WriteError($"Error during set setting '{opts.Code}' value occured.");
				ReportFailure(ex, operationLabel);
				return 1;
			} catch (Exception ex) {
				//A LOCAL fault keeps its own message (PR #1374 review). Issue #1333 is about
				//server-authored text; a FileNotFoundException from `--file`, an IOException, a
				//JsonException from PrepareUpdateValue or an ArgumentException from the create-argument
				//validation is clio's own prose and names the thing that has to be fixed. Routing those
				//through ReportFailure printed "no cause could be determined ... retry the operation" and
				//never named the file - and CategorizeFailure sends UnauthorizedAccessException to
				//Authentication, telling the operator to repair credentials for a local permission
				//problem.
				_logger.WriteError($"Error during set setting '{opts.Code}' value occured.");
				_logger.WriteError($"Failed {operationLabel}: {ex.GetReadableMessageException()}");
				return 1;
			}
			return 0;
		}

		/// <summary>
		/// Reads the All-Users default value of a sys-setting by code and returns a structured result.
		/// Routes through <see cref="ISysSettingsManager.GetAllUsersDefaultWithType"/> so the resolved
		/// value-type-name is available alongside the value; SecureText values are masked before they
		/// leave the manager so this read path does not bypass the masking applied by list-sys-settings.
		/// Categorizes network, authentication, and validation failures into a non-throwing error
		/// envelope for MCP callers.
		/// </summary>
		public SysSettingGetResult TryGetSysSetting(GetSysSettingArgs args) => ReadSysSetting(args, report: true);

		/// <summary>
		/// The same read, classified but NOT logged - for a caller that treats a failed read as a normal,
		/// expected outcome and surfaces nothing.
		/// </summary>
		/// <remarks>
		/// PR #1373 review. <c>TryGetSysSetting</c> is not sys-setting-tool-only: <c>SetLogoCommand</c>'s
		/// <c>ReadCompanionIsOn</c> probes a companion setting and reads any failure as "the companion is off".
		/// That path was completely silent before the classifier was wired in; afterwards every probe against an
		/// environment where the setting is simply absent minted a correlation ID and wrote a red
		/// <c>[ERR] … (correlation-id: …)</c> line whose ID appears in no result anywhere - while the command went
		/// on to report success. That is the inverse of the invariant <see cref="CategorizeAndLog"/> exists for:
		/// an ID in a log line no result mentions is the same defect as an ID on a result no log line mentions. On
		/// the MCP <c>set-logo</c> surface those lines can also ride along with a <c>success: true</c> response and
		/// read to an agent as evidence of failure.
		/// </remarks>
		public SysSettingGetResult TryGetSysSettingQuietly(GetSysSettingArgs args) =>
			ReadSysSetting(args, report: false);

		private SysSettingGetResult ReadSysSetting(GetSysSettingArgs args, bool report) {
			try {
				if (string.IsNullOrWhiteSpace(args.Code)) {
					throw new ArgumentException("code is required.");
				}
				(string value, string typeName) = _sysSettingsManager.GetAllUsersDefaultWithType(args.Code);
				string maskedValue = ApplySecureTextMask(typeName, value ?? string.Empty);
				return new SysSettingGetResult(true, args.Code, maskedValue);
			} catch (Exception ex) {
				//Classification is unconditional; only the LOG LINE is the caller's choice. A probe still gets
				//the category and cause on its envelope if it wants them - it just does not put a red line and
				//an unreferenced correlation ID in front of an operator whose command is going to succeed.
				SysSettingFailure failure = report
					? ReportFailure(ex, ReadOperationLabel)
					: CategorizeFailure(ex, ReadOperationLabel, _correlationIds.New());
				return new SysSettingGetResult(false, args.Code, string.Empty, failure.Error,
					failure.Category, failure.Cause, failure.RecoveryAction, failure.CorrelationId);
			}
		}

		private const string SecureTextValueTypeName = "SecureText";
		private const string MaskedSecureValuePlaceholder = "***";
		// VwSysSetting.GetDefaultValue returns this sentinel when no SysSettingsValue row is found
		// for a setting. Treat it as "unconfigured" rather than "real value to mask".
		private const string DefValueUnconfiguredSentinel = "undefined";

		/// <summary>
		/// Centralized SecureText masking applied to every value the MCP sys-setting surface surfaces:
		/// list-sys-settings catalog rows, get-sys-setting reads, and the readback values returned by
		/// update-sys-setting / create-sys-setting after the write succeeds. Without this helper the
		/// get/update/create read paths would expose ciphertext through the structured response and
		/// bypass the masking that list-sys-settings already applies.
		/// </summary>
		private static string ApplySecureTextMask(string valueTypeName, string rawValue) {
			if (!string.Equals(valueTypeName, SecureTextValueTypeName, StringComparison.Ordinal)) {
				return rawValue;
			}
			bool isUnconfigured = string.IsNullOrEmpty(rawValue)
				|| string.Equals(rawValue, DefValueUnconfiguredSentinel, StringComparison.Ordinal);
			return isUnconfigured ? string.Empty : MaskedSecureValuePlaceholder;
		}

		// Placeholder surfaced for Binary values in list-sys-settings: the metadata (code/name/type) is
		// useful for discovery (e.g. a branding agent finding LogoImage), but the blob itself cannot be
		// read back, so the value column shows this marker instead of an empty or misleading string.
		private const string BinaryValuePlaceholder = "<binary>";

		/// <summary>
		/// Returns the catalog of sys-settings on the target environment with code, display name, value-type, default value, and cacheable/personal flags.
		/// Binary-type settings (whose value is stored as blob data, e.g. the logo) ARE listed so callers can discover them, but their value column shows
		/// <c>&lt;binary&gt;</c> because the MCP read surface does not return the blob (clio's SysSettingsValue model maps no
		/// binary column; the CLI get-syssetting returns the raw Base64) — write them with update-sys-setting using value-file-path.
		/// SecureText values are masked: the metadata row is returned but the actual stored secret is replaced with a placeholder
		/// so the catalog cannot be used to harvest secrets.
		/// </summary>
		public SysSettingsListResult TryListSysSettings(ListSysSettingsArgs args) {
			try {
				List<SysSettings> settings = _sysSettingsManager.GetAllSysSettingsWithValues(includeBinary: true);
				SysSettingItem[] items = settings
					.Select(setting => new SysSettingItem(
						setting.Code,
						setting.Name,
						setting.ValueTypeName,
						FormatListValue(setting),
						setting.IsCacheable,
						setting.IsPersonal))
					.ToArray();
				return new SysSettingsListResult(true, items);
			} catch (Exception ex) {
				SysSettingFailure failure = ReportFailure(ex, "listing sys-settings");
				return new SysSettingsListResult(false, Array.Empty<SysSettingItem>(), failure.Error,
					failure.Category, failure.Cause, failure.RecoveryAction, failure.CorrelationId);
			}
		}

		private static string FormatListValue(SysSettings setting) =>
			string.Equals(setting.ValueTypeName, BinaryTypeName, StringComparison.Ordinal)
				? BinaryValuePlaceholder
				: ApplySecureTextMask(setting.ValueTypeName, setting.DefValue);

		/// <summary>
		/// Creates a new sys-setting with the supplied metadata. For <c>Lookup</c> settings resolves the
		/// reference entity schema UId by name. Applies the optional initial value via the same code path
		/// as <see cref="TryUpdateSysSetting"/>, so the surfaced result includes the assigned value.
		/// </summary>
		public SysSettingCreateResult TryCreateSysSetting(CreateSysSettingArgs args) {
			try {
				ValidateCreateArgs(args);
				// A Binary initial value is inline Base64 (create has no value-file-path), so it is subject
				// to the same file-security gate as an inline update — checked before anything is created.
				if (args.Value is not null) {
					RejectInlineBinaryUnderActivePolicy(args.Code,
						string.Equals(args.ValueTypeName, BinaryTypeName, StringComparison.Ordinal));
				}
				Guid? referenceSchemaUId = ResolveReferenceSchemaUId(args);
				SysSettingsManager.InsertSysSettingResponse response = _sysSettingsManager.InsertSysSetting(
					args.Name,
					args.Code,
					args.ValueTypeName,
					args.IsCacheable ?? true,
					args.Description ?? string.Empty,
					args.IsPersonal ?? false,
					referenceSchemaUId);
				if (!response.Success) {
					//A create can fail WITHOUT an exception: the platform answers with success:false and its
					//own prose. That prose used to become `error` verbatim - server-authored text on the one
					//field an agent reads (issue #1333) - and the envelope carried none of the classified
					//parts issue #1329 requires. Both are composed here instead.
					SysSettingFailure failure = ReportProviderFailure(
						response.ResponseStatus?.Message, CreateOperationLabel);
					return new SysSettingCreateResult(false, args.Code, args.ValueTypeName, null,
						failure.Error, Warning: null, failure.Category, failure.Cause,
						failure.RecoveryAction, failure.CorrelationId);
				}
				return ApplyInitialValue(args);
			} catch (Exception ex) {
				SysSettingFailure failure = ReportFailure(ex, CreateOperationLabel);
				return new SysSettingCreateResult(false, args.Code, args.ValueTypeName, null, failure.Error,
					Warning: null, failure.Category, failure.Cause, failure.RecoveryAction,
					failure.CorrelationId);
			}
		}

		private static void ValidateCreateArgs(CreateSysSettingArgs args) {
			if (string.IsNullOrWhiteSpace(args.Code)) {
				throw new ArgumentException("code is required.");
			}
			if (string.IsNullOrWhiteSpace(args.Name)) {
				throw new ArgumentException("name is required.");
			}
			if (string.IsNullOrWhiteSpace(args.ValueTypeName)) {
				throw new ArgumentException("value-type-name is required.");
			}
			if (!SupportedValueTypeNames.Contains(args.ValueTypeName, StringComparer.Ordinal)) {
				throw new ArgumentException(
					$"Unsupported value-type-name '{args.ValueTypeName}'. Allowed values: " +
					string.Join(", ", SupportedValueTypeNames) + ".");
			}
			if (args.ValueTypeName == LookupTypeName
				&& string.IsNullOrWhiteSpace(args.ReferenceSchemaName)) {
				throw new ArgumentException(
					"reference-schema-name is required when value-type-name is 'Lookup'.");
			}
		}

		private Guid? ResolveReferenceSchemaUId(CreateSysSettingArgs args) {
			if (args.ValueTypeName != LookupTypeName
				|| string.IsNullOrWhiteSpace(args.ReferenceSchemaName)) {
				return null;
			}
			Guid? uId = _sysSettingsManager.FindSchemaUIdByName(args.ReferenceSchemaName);
			if (uId is null) {
				throw new ArgumentException(
					$"Entity schema '{args.ReferenceSchemaName}' was not found on the target environment.");
			}
			return uId;
		}

		private SysSettingCreateResult ApplyInitialValue(CreateSysSettingArgs args) {
			if (args.Value is null) {
				return new SysSettingCreateResult(true, args.Code, args.ValueTypeName);
			}
			bool updated = _sysSettingsManager.UpdateSysSetting(args.Code, args.Value, args.ValueTypeName);
			if (!updated) {
				//Partial success, so there is no Error - but the ID and the line that carries it are ONE
				//operation here too (PR #1374 review). The manager's own "SysSettings with code: {code} is
				//not updated." lines carry no correlation ID and never have, so minting a bare token here
				//handed the caller something to grep that resolved to nothing - the exact failure
				//CategorizeAndLog exists to prevent, and worse on the MCP path, where an agent cannot tell
				//"the line is below my verbosity" from "the line does not exist".
				string correlationId = _correlationIds.New();
				_logger.WriteError(
					$"Sys-setting '{args.Code}' was created, but the initial value could not be applied. "
					+ $"(correlation-id: {correlationId})");
				return new SysSettingCreateResult(true, args.Code, args.ValueTypeName, null,
					Error: null,
					Warning: "Sys-setting was created, but the initial value could not be applied.",
					CorrelationId: correlationId);
			}
			string assignedValue = _sysSettingsManager.GetAllUsersDefaultByCode(args.Code);
			string maskedAssignedValue = ApplySecureTextMask(args.ValueTypeName, assignedValue);
			return new SysSettingCreateResult(true, args.Code, args.ValueTypeName, maskedAssignedValue);
		}

		/// <summary>
		/// The legacy single-line categorization, kept for callers that surface only the message.
		/// </summary>
		/// <remarks>
		/// Delegates to <see cref="CategorizeFailure"/> so there is one classification, not two. Prefer
		/// <see cref="CategorizeFailure"/>: this overload drops the actionable cause, the recovery action
		/// and the correlation ID, which is what issue #1329 was about.
		/// </remarks>
		internal static string CategorizeError(Exception ex, string operationLabel) =>
			CategorizeFailure(ex, operationLabel, correlationId: null).Error;

		/// <summary>
		/// Classifies a failure into the structured envelope the sys-setting results carry: the legacy
		/// message, the category an agent branches on, a cause, a recovery action, and the correlation ID
		/// that finds the log line written for the same failure.
		/// </summary>
		/// <param name="ex">The failure to classify.</param>
		/// <param name="operationLabel">The operation, as it reads inside the legacy message.</param>
		/// <param name="correlationId">The ID issued for this operation, or <see langword="null"/>.</param>
		internal static SysSettingFailure CategorizeFailure(Exception ex, string operationLabel,
			string correlationId) {
			//The Creatio client reaches transport faults through Task.Result, which wraps them in an
			//AggregateException. Switching on the outer type alone therefore saw the wrapper, not the
			//fault, and an aggregate carrying an AuthenticationException or a typed 401 fell through to
			//the generic "Failed ..." - losing exactly the credential diagnosis this command exists to
			//report.
			Exception fault = UnwrapTransportFault(ex);
			return fault switch {
				HttpRequestException httpEx when IsAuthenticationFailure(httpEx)
					=> Authentication(operationLabel, correlationId),
				HttpRequestException => Network(operationLabel, correlationId),
				WebException webEx when IsAuthenticationFailure(webEx)
					=> Authentication(operationLabel, correlationId),
				WebException => Network(operationLabel, correlationId),
				SocketException => Network(operationLabel, correlationId),
				UnauthorizedAccessException => Authentication(operationLabel, correlationId),
				//UNCONDITIONAL, and before the AuthenticationException arms. SessionRejectedException is
				//only ever raised where the rejection was already PROVEN (a raw body carrying Creatio's
				//auth-routing markers, or a corroborated provider verdict), so re-asking the question is
				//not merely redundant - it is wrong. The classifier would run the TLS-prose regex over
				//this exception's own message, and that message interpolates the operation label, which
				//carries the caller's operand: reading sys-setting 'SslCertificateThumbprint' matches
				///certificate/ and flipped a proven credential rejection to "Network error".
				SessionRejectedException => Authentication(operationLabel, correlationId),
				//A bare AuthenticationException is asked the same question as the wrapped ones: the framework
				//raises this type for a TLS handshake too, and a bad server certificate reported as rejected
				//credentials hides the only diagnosis that leads to the fix.
				AuthenticationException authEx when IsAuthenticationFailure(authEx)
					=> Authentication(operationLabel, correlationId),
				AuthenticationException => Network(operationLabel, correlationId),
				//An aggregate that carries several distinct faults is not unwrapped, because no single
				//inner represents it - but a credential failure among them still has to be reported as one.
				AggregateException aggregate when IsAuthenticationFailure(aggregate)
					=> Authentication(operationLabel, correlationId),
				//A gateway/WAF page reaching JsonSerializer.Deserialize raises JsonException, which had no arm:
				//the operator was told "Failed creating sys-setting." with no cause, on the very half of the
				//write path the removed preflight probe used to diagnose. ThrowIfSessionRejected only fires
				//when the body PROVES a rejected session, so every other non-JSON answer lands here.
				JsonException => new SysSettingFailure(
					$"Creatio returned a non-JSON response {operationLabel}.",
					SysSettingErrorCategories.Network, SysSettingFailureTexts.NonJsonResponseCause,
					SysSettingFailureTexts.NonJsonResponseRecovery, correlationId),
				//BOUNDED and REDACTED wherever an exception MESSAGE is promoted into a caller-visible field.
				//These arms return the message of ANY exception of those types raised anywhere below, and such
				//messages are unbounded and can carry paths, URLs or response fragments.
				ArgumentException argEx => new SysSettingFailure(SafeDetail(argEx.Message),
					SysSettingErrorCategories.Validation, SafeDetail(argEx.Message),
					SysSettingFailureTexts.ValidationRecovery, correlationId),
				//DataProviderFailureException is the one InvalidOperationException whose message IS the
				//diagnosis - it is composed locally by ClassifyingDataProvider from a response that carries
				//no exception of its own. An ordinary InvalidOperationException keeps its message too (that
				//is the pre-existing behaviour) but is not claimed to be a provider verdict.
				DataProviderFailureException providerEx => new SysSettingFailure(SafeDetail(providerEx.Message),
					SysSettingErrorCategories.ProviderFailure, SafeDetail(providerEx.Message),
					SysSettingFailureTexts.ProviderFailureRecovery, correlationId),
				InvalidOperationException invEx => new SysSettingFailure(SafeDetail(invEx.Message),
					SysSettingErrorCategories.Unknown, SafeDetail(invEx.Message),
					SysSettingFailureTexts.UnknownRecovery, correlationId),
				//An unresolvable environment is a CONFIGURATION failure, not an unknown one. It used to
				//reach the fallback arm below and be reported as "no cause could be determined" with
				//"retry the operation" - advice that makes an agent loop, when the resolver had already
				//said exactly what to fix. The resolver's text is clio-local (EnvironmentNotFoundError,
				//settings-file paths), so it is safe as the cause; Error keeps the generic label so an
                //unregistered name is still not promoted into the headline message.
				//PR #1373 review: routed on the exception's OWN Reason, not on its type. Four of the resolver's
				//throw sites are authentication and target-URL rejections, and reporting those as
				//Configuration + "register the environment with reg-web-app" is advice a credential-passthrough
				//caller over mcp-http cannot act on - it has no environment to register - while an agent
				//branching on the category will not re-authenticate, because the category says the problem is
				//local configuration. Reason defaults to Configuration, so every unregistered-name site is
				//unchanged.
				EnvironmentResolutionException resolutionEx =>
					DescribeResolutionFailure(resolutionEx, operationLabel, correlationId),
				var _ => new SysSettingFailure($"Failed {operationLabel}.",
					SysSettingErrorCategories.Unknown, SysSettingFailureTexts.UnknownCause,
					SysSettingFailureTexts.UnknownRecovery, correlationId)

			};
		}

		/// <summary>
		/// Classifies an <see cref="EnvironmentResolutionException"/> by what it is actually about. The
		/// resolver's text is clio-local (a settings-file path, an allowlist reason, the missing auth kind), so
		/// it stays safe as the cause; <c>Error</c> keeps the generic label either way, so an unregistered name
		/// is still not promoted into the headline message.
		/// </summary>
		private static SysSettingFailure DescribeResolutionFailure(EnvironmentResolutionException resolutionEx,
			string operationLabel, string correlationId) {
			(string category, string recovery) = resolutionEx.Reason switch {
				EnvironmentResolutionReason.Authentication => (SysSettingErrorCategories.Authentication,
					SysSettingFailureTexts.PassthroughAuthenticationRecovery),
				EnvironmentResolutionReason.Validation => (SysSettingErrorCategories.Validation,
					SysSettingFailureTexts.RefusedTargetRecovery),
				var _ => (SysSettingErrorCategories.Configuration,
					SysSettingFailureTexts.ConfigurationRecovery),
			};
			return new SysSettingFailure($"Failed {operationLabel}.", category, resolutionEx.Message, recovery,
				correlationId);
		}

		private static SysSettingFailure Authentication(string operationLabel, string correlationId) =>
			new($"Authentication error {operationLabel}.", SysSettingErrorCategories.Authentication,
				SysSettingFailureTexts.AuthenticationCause,
				SysSettingFailureTexts.AuthenticationRecovery, correlationId);

		private static SysSettingFailure Network(string operationLabel, string correlationId) =>
			new($"Network error {operationLabel}.", SysSettingErrorCategories.Network,
				SysSettingFailureTexts.NetworkCause, SysSettingFailureTexts.NetworkRecovery,
				correlationId);

		/// <summary>
		/// Writes the failure to the log with its correlation ID and returns it, so the envelope the caller
		/// builds and the line an operator greps carry the SAME ID.
		/// </summary>
		/// <remarks>
		/// The line cannot corrupt the stdio transport: <see cref="ConsoleLogger"/> suppresses the console
		/// DRAIN in MCP server mode, because stdout there frames JSON-RPC. That is NOT the same as the
		/// line being invisible to a caller - the logger still captures into the per-flow buffer
		/// <c>BaseTool</c> harvests into <c>CommandExecutionResult.Messages</c> - which is why what goes
		/// onto this channel is a fixed local diagnostic, and why the debug excerpt beside it is scrubbed
		/// and fenced at the point of writing.
		/// </remarks>
		private SysSettingFailure ReportFailure(Exception ex, string operationLabel) =>
			//CategorizeAndLog now writes the debug excerpt itself, so every caller of it - not only this
			//one - gets the line the correlation ID bridges to. Writing it again here would emit the
			//excerpt twice for the same failure.
			CategorizeAndLog(ex, operationLabel, _logger, _correlationIds);

		/// <summary>
		/// The non-exception counterpart of <see cref="ReportFailure"/>: classifies a refusal the environment
		/// reported as data (a <c>Success == false</c> response, or a <c>false</c> return) rather than by throwing,
		/// mints its correlation ID from the same provider and writes the same log line, so a caller quoting the ID
		/// finds a record whichever way the failure arrived.
		/// </summary>
		private SysSettingFailure ReportRefusal(string operationLabel, string category, string cause,
			string recoveryAction) {
			SysSettingFailure failure = new($"Failed {operationLabel}.", category, cause, recoveryAction,
				_correlationIds.New());
			WriteAndForwardFailureLine(_logger, failure);
			return failure;
		}

		/// <summary>
		/// Classifies a failure AND writes the one log line that carries its correlation ID, for callers
		/// that hold no command instance - the MCP tools' environment-resolution catch blocks.
		/// </summary>
		/// <remarks>
		/// A correlation ID on a result that no log line mentions is worse than no ID at all: it invites
		/// the caller to quote a token that finds nothing. So minting the ID and writing the line are one
		/// operation, and every site that reports a failure goes through here.
		/// </remarks>
		internal static SysSettingFailure CategorizeAndLog(Exception ex, string operationLabel,
			ILogger logger, IOperationCorrelationIdProvider correlationIds) {
			SysSettingFailure failure = CategorizeFailure(ex, operationLabel, correlationIds.New());
			WriteAndForwardFailureLine(logger, failure);
			//Here too, not only in the instance ReportFailure (PR #1374 review): this overload exists
			//BECAUSE other callers use it - the MCP tools' catch blocks - and those paths were getting a
			//correlation ID on the envelope with no matching debug line to bridge to.
			WriteServerDetailAtDebugVerbosity(logger, ex, failure.CorrelationId);
			return failure;
		}

		/// <summary>
		/// Writes the neutralized server excerpt on the DEBUG channel only, tagged with the same
		/// correlation ID the failure envelope carries.
		/// </summary>
		/// <remarks>
		/// Issue #1333. The excerpt is server-authored text, so it may never appear in <c>error</c>,
		/// <c>cause</c>, the MCP envelope or the default log line - the fixed local diagnostic goes there
		/// instead. It still has to be recoverable, because an operator who cannot see what Creatio
		/// actually said cannot tell an expired password from a misconfigured proxy. The channel is
		/// debug-gated (<c>ConsoleLogger.WriteDebug</c> returns early unless <c>--debug</c> was passed)
		/// and its console drain is suppressed under MCP server mode, and the correlation ID is the bridge
		/// from the reported failure to the line.
		/// </remarks>
		private static void WriteServerDetailAtDebugVerbosity(ILogger logger, Exception ex, string correlationId) {
			//The WHOLE chain, not a single unwrap (PR #1374 review). UnwrapTransportFault only steps
			//through single-inner aggregates and TargetInvocationException, so a carrier re-wrapped by a
			//domain or transport exception - a SessionRejectedException inside an environment failure -
			//lost its excerpt silently: the envelope still looked complete and the operator grepped the
			//correlation ID and found nothing.
			string detail = FindServerDetail(ex);
			//Scrubbed and fenced even here, and that is load-bearing rather than belt-and-braces:
			//ConsoleLogger.WriteDebug suppresses the console DRAIN under MCP server mode but still
			//CAPTURES into the per-flow buffer BaseTool harvests into CommandExecutionResult.Messages. The
			//excerpt reaching this line is only control-character normalized and length-capped, so a
			//bearer token, a target URI or a credential pair inside it would otherwise be intact.
			string safeDetail = SensitiveErrorTextRedactor.RedactUntrustedOrNull(detail);
			if (safeDetail is null) {
				return;
			}
			logger.WriteDebug($"(correlation-id: {correlationId}) server detail: {safeDetail}");
		}

		/// <summary>
		/// <see langword="true"/> when this failure - or anything it wraps - can hold text the SERVER
		/// authored, and therefore has to be reported through the classified envelope rather than by its
		/// own message.
		/// </summary>
		/// <remarks>
		/// PR #1374 review. The CLI write path used to catch <see cref="Exception"/> and route everything
		/// through <see cref="ReportFailure"/>, which is type-blind: a local fault has no arm in
		/// <see cref="CategorizeFailure"/>, so a missing <c>--file</c> lost its path and printed
		/// "no cause could be determined ... retry the operation", and
		/// <see cref="UnauthorizedAccessException"/> - which on this path is a local file permission -
		/// was routed to <c>Authentication</c>, sending the operator to repair working credentials.
		/// <para>
		/// The predicate is the two carrier types plus the transport types, which is exactly the set whose
		/// message can hold platform prose. <see cref="EnvironmentResolutionException"/> is included even
		/// though its text is clio-local, because <see cref="CategorizeFailure"/> has a dedicated arm for
		/// it that gives better advice than its bare message.
		/// </para>
		/// </remarks>
		private static bool CarriesServerText(Exception exception) {
			for (Exception current = exception; current is not null; current = current.InnerException) {
				if (current is AggregateException aggregate) {
					return aggregate.InnerExceptions.Any(CarriesServerText);
				}
				if (current is IServerDetailCarrier
						or HttpRequestException
						or WebException
						or SocketException
						or AuthenticationException
						or EnvironmentResolutionException) {
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The server excerpt of the first <see cref="IServerDetailCarrier"/> anywhere in the exception
		/// chain, including inside single-fault aggregates, or <see langword="null"/> when there is none.
		/// </summary>
		private static string FindServerDetail(Exception exception) {
			for (Exception current = exception; current is not null; current = current.InnerException) {
				if (current is IServerDetailCarrier carrier) {
					return carrier.ServerDetail;
				}
				if (current is AggregateException { InnerExceptions.Count: 1 } aggregate
						&& aggregate.InnerExceptions[0] is IServerDetailCarrier innerCarrier) {
					return innerCarrier.ServerDetail;
				}
			}
			return null;
		}

		/// <summary>
		/// Composes the failure envelope for a provider failure reported WITHOUT an exception - a
		/// <c>success:false</c> response whose only diagnosis is the platform's own prose.
		/// </summary>
		/// <remarks>
		/// The prose is fenced and scrubbed rather than dropped (issue #1333): it is the platform's own
		/// validation text, so no fixed sentence can replace it, but it reaches an agent's context through
		/// the MCP envelope and must therefore be marked as observed data. Composition is shared with
		/// <see cref="ClassifyingDataProvider"/> through
		/// <see cref="ServerReportedFailureText.Describe"/>, so the two cannot drift.
		/// </remarks>
		private SysSettingFailure ReportProviderFailure(string serverMessage, string operationLabel) {
			ServerReportedFailureText described = ServerReportedFailureText.Describe(serverMessage);
			SysSettingFailure failure = new(
				described.ComposeMessage(operationLabel),
				SysSettingErrorCategories.ProviderFailure, described.Cause,
				SysSettingFailureTexts.ProviderFailureRecovery, _correlationIds.New());
			WriteAndForwardFailureLine(_logger, failure);
			return failure;
		}

		/// <summary>
		/// Writes the one log line carrying the failure's correlation ID, and on the MCP path ALSO sends it
		/// to the client as a <c>notifications/message</c> under the <c>clio.tool.{correlationId}</c>
		/// category.
		/// </summary>
		/// <remarks>
		/// PR #1373 review: writing the line alone was not enough to make the ID resolvable. Running as an
		/// MCP server every ordinary sink is closed - <see cref="ConsoleLogger"/> suppresses console writes
		/// under <c>Program.IsMcpServerMode</c>, the log file exists only when the operator passed
		/// <c>--log</c>, and the sys-setting tools and <c>SchemaNamePrefixTool</c> are plain
		/// <c>[McpServerToolType]</c> classes that never flush the way <c>BaseTool</c> does. So the line
		/// reached nobody, while the shipped recovery text tells the caller to quote the ID.
		/// The notification is built from the line directly rather than by draining the shared
		/// <c>PreserveMessages</c> buffer: that buffer belongs to whatever flow is capturing (a
		/// <c>BaseTool</c> parent may be), and clearing it here would swallow messages this failure did not
		/// produce. <c>ForwardMessages</c> no-ops when no MCP server is active, so the CLI path is unchanged.
		/// </remarks>
		private static void WriteAndForwardFailureLine(ILogger logger, SysSettingFailure failure) {
			string line = DescribeFailureForLog(failure);
			logger.WriteError(line);
			McpServer.Tools.McpLogNotifier.ForwardMessages([new ErrorMessage(line)], failure.CorrelationId);
		}

		/// <summary>Renders a classified failure as one log line, correlation ID last.</summary>
		/// <remarks>
		/// The cause is omitted when it is the SAME string as the headline (PR #1374 review). Three arms of
		/// <see cref="CategorizeFailure"/> put one composed diagnostic into both <c>Error</c> and
		/// <c>Cause</c>, so this line printed it twice - and where that diagnostic carries the fenced
		/// server excerpt, twice meant two <c>[untrusted-source-text begin]…[end]</c> pairs on one line.
		/// </remarks>
		internal static string DescribeFailureForLog(SysSettingFailure failure) {
			string cause = string.Equals(failure.Error, failure.Cause, StringComparison.Ordinal)
				? string.Empty
				: $"Cause: {failure.Cause} ";
			return $"{failure.Error} {cause}Action: {failure.RecoveryAction} "
				+ $"(correlation-id: {failure.CorrelationId})";
		}

		/// <summary>
		/// The operation labels these results and log lines read with. Constants because the same label
		/// appears at several report sites for one operation, and because which label a failure carries is
		/// part of the diagnosis - PR #1374 review found the CLI write path reporting a failed CREATE as
		/// "updating sys-setting".
		/// </summary>
		private const string CreateOperationLabel = "creating sys-setting";

		/// <inheritdoc cref="CreateOperationLabel"/>
		private const string UpdateOperationLabel = "updating sys-setting";

		/// <inheritdoc cref="CreateOperationLabel"/>
		private const string ReadOperationLabel = "reading sys-setting";

		// Cap on a message promoted into a user-visible field. 300 is what DataProviderFailureException's
		// detail already uses, so the two paths expose the same amount.
		private const int MaxPromotedMessageLength = 300;

		// Redaction runs BEFORE the cap, deliberately: SensitiveErrorTextRedactor matches a token as a whole
		// unit, so capping first can split one in half and leave the visible fragment unredacted. This is the
		// same order ServiceResponseJsonGuard.BuildPreview uses.
		// The cap itself goes through TruncateWithoutSplittingSurrogatePair rather than a raw slice: Redact
		// only scrubs secrets, it does not touch surrogates, so an astral character straddling the cap point
		// would leave a lone high surrogate in SysSettingFailure.Error/.Cause - and System.Text.Json throws
		// on invalid UTF-16, failing the whole tool response instead of truncating one message.
		private static string SafeDetail(string message) {
			if (string.IsNullOrEmpty(message)) {
				return message;
			}
			string redacted = McpServer.SensitiveErrorTextRedactor.Redact(message);
			return redacted.Length <= MaxPromotedMessageLength
				? redacted
				: TextUtilities.TruncateWithoutSplittingSurrogatePair(redacted, MaxPromotedMessageLength) + "...";
		}
		// Bounds every walk over an exception chain. A chain this deep is not something a transport
		// produces, and the bound is what keeps a hand-built or self-referencing chain from looping.
		private const int MaxExceptionUnwrapDepth = 16;

		/// <summary>
		/// Returns the exception that should be classified: a wrapper carrying exactly one fault unwraps
		/// to that fault, and everything else is returned unchanged.
		/// </summary>
		/// <remarks>
		/// A multi-fault <see cref="AggregateException"/> is deliberately NOT unwrapped - picking its first
		/// inner would report one of several failures as if it were the whole story. Those are handled by
		/// the aggregate arm in <see cref="CategorizeFailure"/> instead.
		/// </remarks>
		private static Exception UnwrapTransportFault(Exception exception) {
			Exception current = exception;
			for (int depth = 0; depth < MaxExceptionUnwrapDepth; depth++) {
				Exception inner = current switch {
					AggregateException aggregate when aggregate.InnerExceptions.Count == 1
						=> aggregate.InnerExceptions[0],
					TargetInvocationException { InnerException: { } target } => target,
					var _ => null
				};
				if (inner is null) {
					return current;
				}
				current = inner;
			}
			return current;
		}

		// A bounded 401 token, not any occurrence of the digits. "Connection refused at
		// http://localhost:40124" is a network error, and reporting it as rejected credentials sends the
		// operator off to fix a working login. The token must also stand alone, so a port or an id containing
		// 401 does not qualify.
		// Delegates to the one shared classifier so this layer and SysSettingsManager cannot answer the
		// same question differently. See AuthenticationFailureClassifier for why that mattered.
		private static bool IsAuthenticationFailure(Exception exception) =>
			AuthenticationFailureClassifier.IsAuthenticationFailure(exception);

		private static string DescribeUnreadableBinaryTarget(string code) {
			return $"Sys-setting '{code}' was not found or is not readable by the current user. Uploading a " +
				"file requires an existing, readable Binary setting.";
		}
	}
}
