using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Clio.UserEnvironment;

namespace Clio.Common.Assertions
{
	/// <summary>
	/// Handles filesystem permission validation assertions.
	/// </summary>
	public class FsPermissionAssertion
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly ILogger _logger;

		public FsPermissionAssertion(ISettingsRepository settingsRepository, ILogger logger)
		{
			_settingsRepository = settingsRepository;
			_logger = logger;
		}

		/// <summary>
		/// Validates that a user has the specified permissions on a path.
		/// </summary>
		/// <param name="pathOrSettingKey">Path to validate or setting key</param>
		/// <param name="userIdentity">Windows user/group identity (e.g., "BUILTIN\IIS_IUSRS" or "IIS APPPOOL\MyApp")</param>
		/// <param name="requiredPermission">Required permission level: read, write, modify, full-control</param>
		/// <returns>AssertionResult with permission validation details</returns>
		public AssertionResult Execute(string pathOrSettingKey, string userIdentity, string requiredPermission)
		{
			// Validate Windows platform
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPerm,
					"Permission assertions are only supported on Windows platform"
				);
			}

			// Validate inputs
			if (string.IsNullOrWhiteSpace(pathOrSettingKey))
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPath,
					"Path parameter is required"
				);
			}

			if (string.IsNullOrWhiteSpace(userIdentity))
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsUser,
					"User identity parameter is required"
				);
			}

			if (string.IsNullOrWhiteSpace(requiredPermission))
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPerm,
					"Permission parameter is required"
				);
			}

			// Resolve path
			string resolvedPath = ResolvePath(pathOrSettingKey);

			// Validate path exists
			if (!Directory.Exists(resolvedPath))
			{
				var result = AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPath,
					$"Path does not exist: {resolvedPath}"
				);
				result.Details["requestedPath"] = pathOrSettingKey;
				result.Details["resolvedPath"] = resolvedPath;
				return result;
			}

			_logger.WriteInfo($"Validating permissions for '{userIdentity}' on '{resolvedPath}'");

			// Parse required permission level
			FileSystemRights requiredRights;
			try
			{
				requiredRights = ParsePermissionLevel(requiredPermission);
			}
			catch (ArgumentException ex)
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPerm,
					ex.Message
				);
			}

			// Check permissions
			try
			{
				bool hasPermission = ValidatePermissions(resolvedPath, userIdentity, requiredRights);

				if (!hasPermission)
				{
					var result = AssertionResult.Failure(
						AssertionScope.Fs,
						AssertionPhase.FsPerm,
						$"User '{userIdentity}' does not have '{requiredPermission}' permission on path '{resolvedPath}'"
					);
					result.Details["requestedPath"] = pathOrSettingKey;
					result.Details["resolvedPath"] = resolvedPath;
					result.Details["userIdentity"] = userIdentity;
					result.Details["requiredPermission"] = requiredPermission;
					return result;
				}

				// Success
				var successResult = AssertionResult.Success();
				successResult.Scope = AssertionScope.Fs;
				successResult.Resolved["path"] = resolvedPath;
				successResult.Resolved["userIdentity"] = userIdentity;
				successResult.Resolved["permission"] = requiredPermission;
				successResult.Details["requestedPath"] = pathOrSettingKey;

				return successResult;
			}
			catch (Exception ex)
			{
				var result = AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPerm,
					$"Error validating permissions: {ex.Message}"
				);
				result.Details["requestedPath"] = pathOrSettingKey;
				result.Details["resolvedPath"] = resolvedPath;
				result.Details["userIdentity"] = userIdentity;
				result.Details["exception"] = ex.GetType().Name;
				return result;
			}
		}

		/// <summary>
		/// Resolves a path string which may be either a setting key or an absolute path.
		/// </summary>
		private string ResolvePath(string pathOrSettingKey)
		{
			if (pathOrSettingKey.Equals("iis-clio-root-path", StringComparison.OrdinalIgnoreCase))
			{
				return _settingsRepository.GetIISClioRootPath();
			}
			return pathOrSettingKey;
		}

		/// <summary>
		/// Parses permission level string into FileSystemRights.
		/// </summary>
		private FileSystemRights ParsePermissionLevel(string permission)
		{
			return permission.ToLowerInvariant() switch
			{
				"read" => FileSystemRights.Read,
				"write" => FileSystemRights.Write,
				"modify" => FileSystemRights.Modify,
				"full-control" => FileSystemRights.FullControl,
				"full" => FileSystemRights.FullControl,
				_ => throw new ArgumentException(
					$"Invalid permission level: '{permission}'. Valid options are: read, write, modify, full-control")
			};
		}

		/// <summary>
		/// Validates that a user has the required permissions on a directory.
		/// </summary>
		private bool ValidatePermissions(string directoryPath, string userIdentity, FileSystemRights requiredRights)
		{
			DirectoryInfo dirInfo = new DirectoryInfo(directoryPath);
			DirectorySecurity dirSecurity = dirInfo.GetAccessControl();
			AuthorizationRuleCollection rules = dirSecurity.GetAccessRules(true, true, typeof(NTAccount));

			// Resolve user identity to account
			IdentityReference identity;
			try
			{
				identity = new NTAccount(userIdentity);
			}
			catch
			{
				_logger.WriteWarning($"Could not resolve user identity: {userIdentity}");
				return false;
			}

			// Check each access rule
			foreach (FileSystemAccessRule rule in rules)
			{
				// Check if rule applies to our user
				if (rule.IdentityReference.Value.Equals(identity.Value, StringComparison.OrdinalIgnoreCase))
				{
					// Check if this is an Allow rule
					if (rule.AccessControlType == AccessControlType.Allow)
					{
						// Check if the rule grants the required rights
						if ((rule.FileSystemRights & requiredRights) == requiredRights)
						{
							_logger.WriteInfo($"Found matching Allow rule with sufficient permissions");
							return true;
						}
					}
				}
			}

			_logger.WriteWarning($"No matching access rule found for '{userIdentity}' with required permissions");
			return false;
		}
	}
}
