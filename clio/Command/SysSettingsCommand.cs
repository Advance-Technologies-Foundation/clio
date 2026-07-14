using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
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

		public SysSettingsCommand(ISysSettingsManager sysSettingsManager, ILogger logger, IFileSystem fileSystem){
			_sysSettingsManager = sysSettingsManager;
			_logger = logger;
			_fileSystem = fileSystem;
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
				throw new ArgumentException(
					$"Sys-setting '{code}' was not found. Create it as a Binary setting before uploading a file.");
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

		public void UpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
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
		}

		public void TryUpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			try {
				UpdateSysSetting(opts, settings);
			} catch {
				_logger.WriteError($"SysSettings with code: {opts.Code} is not updated.");
			}
		}

		/// <summary>
		/// Updates an existing sys-setting value. The provided <c>value-type-name</c> is used only as a
		/// fallback when the setting type cannot be resolved on the target environment.
		/// </summary>
		public SysSettingUpdateResult TryUpdateSysSetting(UpdateSysSettingArgs args) {
			try {
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
				// Resolve the existing type once so file/inline paths share it (avoids a second lookup).
				(_, string existingType) = _sysSettingsManager.GetAllUsersDefaultWithType(args.Code);
				bool targetIsBinary = string.Equals(existingType, BinaryTypeName, StringComparison.Ordinal);
				string value;
				if (hasFilePath) {
					// A file upload targets a Binary setting: confirm the target is Binary and passes the
					// environment's file-security policy before reading the file.
					if (existingType is null) {
						throw new ArgumentException(
							$"Sys-setting '{args.Code}' was not found. Create it as a Binary setting before uploading a file.");
					}
					if (!targetIsBinary) {
						throw new ArgumentException(
							$"Cannot upload a file to sys-setting '{args.Code}': it is type '{existingType}', not Binary. " +
							"A file value can only be written to a Binary setting.");
					}
					EnforceFileSecurityPolicy(args.ValueFilePath);
					value = EncodeFileToBase64(args.ValueFilePath);
				} else {
					// An inline value for a Binary setting is rejected while a policy is active (no extension
					// to validate); when allowed, validate it up front so the caller gets the specific cause.
					RejectInlineBinaryUnderActivePolicy(args.Code, targetIsBinary);
					value = args.Value;
					if (targetIsBinary && !_sysSettingsManager.TryValidateBinaryValue(value, out string binaryError)) {
						return new SysSettingUpdateResult(false, args.Code, null, binaryError);
					}
				}
				// A file-derived payload is Binary by nature; default the type accordingly when it is not
				// resolved from the target environment.
				string fallbackTypeName = hasFilePath ? BinaryTypeName : "Text";
				string valueTypeName = string.IsNullOrWhiteSpace(args.ValueTypeName)
					? fallbackTypeName
					: args.ValueTypeName;
				bool updated = _sysSettingsManager.UpdateSysSetting(args.Code, value, valueTypeName);
				if (!updated) {
					return new SysSettingUpdateResult(false, args.Code, null,
						"Failed to update sys-setting. The setting may not exist, or the value did not match the expected type.");
				}
				(string readback, string readbackType) = _sysSettingsManager.GetAllUsersDefaultWithType(args.Code);
				string maskedReadback = ApplySecureTextMask(readbackType, readback);
				return new SysSettingUpdateResult(true, args.Code, maskedReadback);
			} catch (Exception ex) {
				string message = CategorizeError(ex, "updating sys-setting");
				return new SysSettingUpdateResult(false, args.Code, null, message);
			}
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

			try {
				CreateSysSettingIfNotExists(opts);
				UpdateSysSetting(opts);
			} catch (Exception ex) {
				_logger.WriteError($"Error during set setting '{opts.Code}' value occured with message: {ex.Message}");
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
		public SysSettingGetResult TryGetSysSetting(GetSysSettingArgs args) {
			try {
				if (string.IsNullOrWhiteSpace(args.Code)) {
					throw new ArgumentException("code is required.");
				}
				(string value, string typeName) = _sysSettingsManager.GetAllUsersDefaultWithType(args.Code);
				string maskedValue = ApplySecureTextMask(typeName, value ?? string.Empty);
				return new SysSettingGetResult(true, args.Code, maskedValue);
			} catch (Exception ex) {
				string message = CategorizeError(ex, "reading sys-setting");
				return new SysSettingGetResult(false, args.Code, string.Empty, message);
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
				string message = CategorizeError(ex, "listing sys-settings");
				return new SysSettingsListResult(false, Array.Empty<SysSettingItem>(), message);
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
					string message = response.ResponseStatus?.Message;
					return new SysSettingCreateResult(false, args.Code, args.ValueTypeName, null,
						string.IsNullOrWhiteSpace(message) ? "Failed creating sys-setting." : message);
				}
				return ApplyInitialValue(args);
			} catch (Exception ex) {
				string message = CategorizeError(ex, "creating sys-setting");
				return new SysSettingCreateResult(false, args.Code, args.ValueTypeName, null, message);
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
				return new SysSettingCreateResult(true, args.Code, args.ValueTypeName, null,
					Error: null,
					Warning: "Sys-setting was created, but the initial value could not be applied.");
			}
			string assignedValue = _sysSettingsManager.GetAllUsersDefaultByCode(args.Code);
			string maskedAssignedValue = ApplySecureTextMask(args.ValueTypeName, assignedValue);
			return new SysSettingCreateResult(true, args.Code, args.ValueTypeName, maskedAssignedValue);
		}

		internal static string CategorizeError(Exception ex, string operationLabel) {
			return ex switch {
				HttpRequestException => $"Network error {operationLabel}.",
				WebException => $"Network error {operationLabel}.",
				SocketException => $"Network error {operationLabel}.",
				UnauthorizedAccessException => $"Authentication error {operationLabel}.",
				AuthenticationException => $"Authentication error {operationLabel}.",
				ArgumentException argEx => argEx.Message,
				InvalidOperationException invEx => invEx.Message,
				_ => $"Failed {operationLabel}."
			};
		}
	}
}
