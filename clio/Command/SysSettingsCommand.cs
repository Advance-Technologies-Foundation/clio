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

		[Value(1, MetaName = "Value", Required = false, HelpText = "Sys-setting Value")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText = "Type", Default = "Text")]
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

		// Binary is intentionally excluded from the MCP create/update surface: the
		// platform's PostSysSettingsValues endpoint is scalar-only, and the
		// SysSettingsValue model does not expose a BinaryValue column, so get-sys-setting
		// cannot read a binary value back either. Binary sys-settings need a dedicated
		// upload/download path that is out of scope for this PR.
		private static readonly string[] SupportedValueTypeNames = [
			"Text", "ShortText", "MediumText", "LongText", "SecureText", "MaxSizeText",
			"Boolean", "DateTime", "Date", "Time", "Integer",
			"Money", "Float", LookupTypeName,
			"Currency", "Decimal"
		];

		private readonly ISysSettingsManager _sysSettingsManager;
		private readonly ILogger _logger;

		public SysSettingsCommand(ISysSettingsManager sysSettingsManager, ILogger logger){
			_sysSettingsManager = sysSettingsManager;
			_logger = logger;
		}

		private void CreateSysSettingIfNotExists(SysSettingsOptions opts) {
			_sysSettingsManager.CreateSysSettingIfNotExists(opts.Code, opts.Code, opts.Type);
		}

		public void UpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			bool isUpdated = _sysSettingsManager.UpdateSysSetting(opts.Code, opts.Value);
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
				if (args.Value is null) {
					throw new ArgumentException("value is required.");
				}
				string valueTypeName = string.IsNullOrWhiteSpace(args.ValueTypeName) ? "Text" : args.ValueTypeName;
				bool updated = _sysSettingsManager.UpdateSysSetting(args.Code, args.Value, valueTypeName);
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

		/// <summary>
		/// Returns the catalog of sys-settings on the target environment with code, display name, value-type, default value, and cacheable/personal flags.
		/// Binary-type settings are excluded from the result — Binary read/write is not exposed through this MCP tool set
		/// (no BinaryValue column in SysSettingsValue and PostSysSettingsValues is scalar-only), so listing them would be misleading.
		/// SecureText values are masked: the metadata row is returned but the actual stored secret is replaced with a placeholder
		/// so the catalog cannot be used to harvest secrets.
		/// </summary>
		public SysSettingsListResult TryListSysSettings(ListSysSettingsArgs args) {
			try {
				List<SysSettings> settings = _sysSettingsManager.GetAllSysSettingsWithValues();
				SysSettingItem[] items = settings
					.Where(setting => !string.Equals(setting.ValueTypeName, "Binary", StringComparison.Ordinal))
					.Select(setting => new SysSettingItem(
						setting.Code,
						setting.Name,
						setting.ValueTypeName,
						MaskSecureValue(setting),
						setting.IsCacheable,
						setting.IsPersonal))
					.ToArray();
				return new SysSettingsListResult(true, items);
			} catch (Exception ex) {
				string message = CategorizeError(ex, "listing sys-settings");
				return new SysSettingsListResult(false, Array.Empty<SysSettingItem>(), message);
			}
		}

		private static string MaskSecureValue(SysSettings setting) =>
			ApplySecureTextMask(setting.ValueTypeName, setting.DefValue);

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
