using System;
using System.Collections.Generic;
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

		// A sys-setting value is meant for a logo/small image; this cap guards against a mistaken path
		// (e.g. pointing at a database backup) being read fully into memory and Base64-encoded. It is a
		// sanity limit, not a policy — 10 MB comfortably covers any real logo or background image.
		private const long MaxBinaryFileSizeBytes = 10L * 1024 * 1024;

		/// <summary>
		/// Reads the file at <paramref name="filePath"/> and returns its Base64-encoded contents.
		/// Used to turn a file's contents (e.g. the logo, or any blob) into the Base64 payload a Binary sys-setting expects,
		/// keeping the bytes on disk rather than in the CLI/MCP arguments. Rejects files larger than
		/// <see cref="MaxBinaryFileSizeBytes"/> so a wrong path fails fast instead of exhausting memory.
		/// </summary>
		private string EncodeFileToBase64(string filePath){
			if (!_fileSystem.ExistsFile(filePath)) {
				throw new ArgumentException($"File not found: '{filePath}'.");
			}
			long sizeBytes = _fileSystem.GetFileSize(filePath);
			if (sizeBytes > MaxBinaryFileSizeBytes) {
				throw new ArgumentException(
					$"File '{filePath}' is {sizeBytes:N0} bytes, which exceeds the " +
					$"{MaxBinaryFileSizeBytes:N0}-byte limit for a Binary sys-setting value.");
			}
			_logger.WriteInfo($"Reading Binary sys-setting value from file '{filePath}' ({sizeBytes} bytes).");
			return Convert.ToBase64String(_fileSystem.ReadAllBytes(filePath));
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
					value = EncodeFileToBase64(opts.Value);
				} else if (LooksLikeFilePath(opts.Value)) {
					// The value looks like a path (Base64 never contains '.', '\\' or ':') but no such file
					// exists — report that plainly instead of letting it fail later as "invalid Base64".
					throw new ArgumentException(
						$"File not found: '{opts.Value}'. For a Binary setting pass a path to an existing " +
						"file (e.g. the logo), or a Base64 string.");
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
				// A file path is read and Base64-encoded locally; such payloads are Binary by nature, so
				// default the type accordingly when it is not resolved from the target environment.
				string value = hasFilePath ? EncodeFileToBase64(args.ValueFilePath) : args.Value;
				string valueTypeName = string.IsNullOrWhiteSpace(args.ValueTypeName)
					? (hasFilePath ? BinaryTypeName : "Text")
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
