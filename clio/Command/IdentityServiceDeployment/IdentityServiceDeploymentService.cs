using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clio.Common;
using Clio.Common.IIS;
using Clio.UserEnvironment;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Clio.Command.IdentityServiceDeployment;

/// <summary>
/// Deploys IdentityService and updates the related Creatio and clio configuration.
/// </summary>
public interface IIdentityServiceDeploymentService
{
	/// <summary>
	/// Deploys IdentityService for the specified target environment.
	/// </summary>
	/// <param name="options">Deployment options.</param>
	/// <returns>The deployment result.</returns>
	IdentityServiceDeploymentResult Deploy(DeployIdentityOptions options);
}

/// <summary>
/// Result returned by the IdentityService deployment command.
/// </summary>
/// <param name="Success">Whether deployment completed.</param>
/// <param name="Message">Human-readable status message.</param>
/// <param name="IdentityServiceUrl">IdentityService base URL.</param>
/// <param name="ClientId">OAuth client id stored in clio settings.</param>
public sealed record IdentityServiceDeploymentResult(
	bool Success,
	string Message,
	string IdentityServiceUrl,
	string ClientId);

/// <summary>
/// Resolves a standalone IdentityService archive from either direct or bundled input.
/// </summary>
public interface IIdentityServiceArchiveResolver
{
	/// <summary>
	/// Returns a standalone IdentityService zip path for the supplied archive.
	/// </summary>
	/// <param name="zipFile">Standalone IdentityService or Creatio distribution zip file.</param>
	/// <param name="identityArchivePathInBundle">Nested archive path inside a distribution zip.</param>
	/// <returns>The resolved standalone IdentityService zip path.</returns>
	string Resolve(string zipFile, string identityArchivePathInBundle);
}

/// <summary>
/// Calls Creatio OAuth configuration endpoints used by IdentityService deployment.
/// </summary>
public interface IIdentityServiceCreatioClient
{
	/// <summary>
	/// Reads the designer IdentityService client secret from Creatio.
	/// </summary>
	string GetDesignerClientSecret();

	/// <summary>
	/// Creates a technical user and returns its identifier.
	/// </summary>
	/// <param name="systemUserName">Technical user name.</param>
	string CreateTechnicalUser(string systemUserName);

	/// <summary>
	/// Creates a clio OAuth client bound to a Creatio system user.
	/// </summary>
	/// <param name="options">Deployment options that define the client metadata.</param>
	/// <param name="systemUserId">Creatio system user identifier.</param>
	OAuthClientCredentials CreateClioClient(DeployIdentityOptions options, string systemUserId);
}

/// <inheritdoc />
public sealed class IdentityServiceArchiveResolver : IIdentityServiceArchiveResolver
{
	/// <inheritdoc />
	public string Resolve(string zipFile, string identityArchivePathInBundle) {
		if (string.IsNullOrWhiteSpace(zipFile)) {
			throw new ArgumentException("--zip-file is required.", nameof(zipFile));
		}
		if (!File.Exists(zipFile)) {
			throw new FileNotFoundException("IdentityService archive was not found.", zipFile);
		}
		if (IsStandaloneIdentityArchive(zipFile)) {
			return zipFile;
		}
		string nestedPath = string.IsNullOrWhiteSpace(identityArchivePathInBundle)
			? "IdentityService.zip"
			: identityArchivePathInBundle.Replace('\\', '/');
		using ZipArchive archive = ZipFile.OpenRead(zipFile);
		ZipArchiveEntry entry = archive.Entries.FirstOrDefault(item =>
			string.Equals(item.FullName.Replace('\\', '/'), nestedPath, StringComparison.OrdinalIgnoreCase));
		if (entry is null) {
			throw new InvalidOperationException(
				$"Archive '{zipFile}' is neither a standalone IdentityService zip nor a bundle containing '{nestedPath}'.");
		}
		string tempDirectory = Path.Combine(Path.GetTempPath(), "clio-identityservice", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		string extractedPath = Path.Combine(tempDirectory, Path.GetFileName(nestedPath));
		entry.ExtractToFile(extractedPath);
		return extractedPath;
	}

	private static bool IsStandaloneIdentityArchive(string zipFile) {
		using ZipArchive archive = ZipFile.OpenRead(zipFile);
		return archive.Entries.Any(item =>
			item.FullName.EndsWith("IdentityService.dll", StringComparison.OrdinalIgnoreCase)
			|| item.FullName.EndsWith("Dockerfile-OAuth", StringComparison.OrdinalIgnoreCase)
			|| item.FullName.Replace('\\', '/').Contains("Data/Migrations/", StringComparison.OrdinalIgnoreCase));
	}
}

/// <inheritdoc />
public sealed class IdentityServiceCreatioClient(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder) : IIdentityServiceCreatioClient
{
	/// <inheritdoc />
	public string GetDesignerClientSecret() {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OAuthConfigGetIdentityServerClientSecret);
		string response = applicationClient.ExecutePostRequest(url, "{}");
		string secret = IdentityServiceDeploymentService.ExtractFirstString(response, "clientSecret", "secret", "value", "result");
		return secret;
	}

	/// <inheritdoc />
	public string CreateTechnicalUser(string systemUserName) {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OAuthConfigCreateTechnicalUser);
		string response = applicationClient.ExecutePostRequest(url, JsonSerializer.Serialize(new {
			createTechnicalUserRequest = new {
				name = systemUserName
			}
		}));
		string id = IdentityServiceDeploymentService.ExtractFirstString(response, "systemUserId", "id", "value", "result");
		if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out Guid parsedId) || parsedId == Guid.Empty) {
			throw new InvalidOperationException("OAuthConfigService/CreateTechnicalUser did not return a systemUserId.");
		}
		return id;
	}

	/// <inheritdoc />
	public OAuthClientCredentials CreateClioClient(DeployIdentityOptions options, string systemUserId) {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OAuthConfigAddClient);
		var request = new Dictionary<string, object> {
			["name"] = options.ClientName,
			["applicationUrl"] = options.ClientApplicationUrl,
			["description"] = options.ClientDescription,
			["systemUserId"] = systemUserId,
			["grantType"] = "client_credentials",
			["allowedGrantTypes"] = new[] { "client_credentials" }
		};
		string response = applicationClient.ExecutePostRequest(url, JsonSerializer.Serialize(new {
			addClientRequest = request
		}));
		string clientId = IdentityServiceDeploymentService.ExtractFirstString(response, "clientId", "ClientId", "client_id");
		string clientSecret = IdentityServiceDeploymentService.ExtractFirstString(response, "clientSecret", "ClientSecret", "secret", "Secret");
		if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) {
			throw new InvalidOperationException("OAuthConfigService/AddClient did not return client credentials.");
		}
		return new OAuthClientCredentials(clientId, clientSecret);
	}
}

/// <summary>
/// OAuth client credentials returned by Creatio.
/// </summary>
/// <param name="ClientId">OAuth client identifier.</param>
/// <param name="ClientSecret">OAuth client secret.</param>
public sealed record OAuthClientCredentials(string ClientId, string ClientSecret);

/// <summary>
/// Grants Creatio roles required by the clio OAuth technical user.
/// </summary>
public interface IIdentityServiceRoleGrantService
{
	/// <summary>
	/// Grants the System administrators role to a technical user.
	/// </summary>
	/// <param name="environment">Target Creatio environment.</param>
	/// <param name="systemUserId">Technical user identifier.</param>
	void GrantSystemAdministratorRole(EnvironmentSettings environment, string systemUserId);
}

/// <summary>
/// Resolves existing Creatio system users used by OAuth client bindings.
/// </summary>
public interface IIdentityServiceSystemUserResolver
{
	/// <summary>
	/// Resolves a Creatio system user identifier by name.
	/// </summary>
	/// <param name="environment">Target Creatio environment.</param>
	/// <param name="systemUserName">Existing system user name.</param>
	/// <returns>The existing system user identifier.</returns>
	string ResolveSystemUserId(EnvironmentSettings environment, string systemUserName);
}

/// <inheritdoc />
public sealed class IdentityServiceRoleGrantService : IIdentityServiceRoleGrantService
{
	private const string SystemAdministratorsRoleName = "System administrators";

	/// <inheritdoc />
	public void GrantSystemAdministratorRole(EnvironmentSettings environment, string systemUserId) {
		if (!Guid.TryParse(systemUserId, out Guid userId) || userId == Guid.Empty) {
			throw new ArgumentException("System user id must be a non-empty GUID.", nameof(systemUserId));
		}
		string connectionString = ReadCreatioDbConnectionString(environment);
		if (IsPostgres(connectionString)) {
			GrantSystemAdministratorRolePostgres(connectionString, userId);
		} else {
			GrantSystemAdministratorRoleSqlServer(connectionString, userId);
		}
	}

	private static bool IsPostgres(string connectionString) =>
		connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
		|| (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
			&& connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase)
			&& !connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase));

	private static string ReadCreatioDbConnectionString(EnvironmentSettings environment) {
		if (string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
			throw new InvalidOperationException("The target environment does not have EnvironmentPath configured.");
		}
		string connectionStringsPath = Path.Combine(environment.EnvironmentPath, "ConnectionStrings.config");
		if (!File.Exists(connectionStringsPath)) {
			throw new FileNotFoundException("Creatio ConnectionStrings.config was not found.", connectionStringsPath);
		}
		XDocument document = XDocument.Load(connectionStringsPath);
		XElement element = document.Root?.Elements("add")
			.FirstOrDefault(item => string.Equals(item.Attribute("name")?.Value, "dbPostgreSql", StringComparison.OrdinalIgnoreCase))
			?? document.Root?.Elements("add")
				.FirstOrDefault(item => string.Equals(item.Attribute("name")?.Value, "db", StringComparison.OrdinalIgnoreCase));
		return element?.Attribute("connectionString")?.Value
			?? throw new InvalidOperationException("ConnectionStrings.config does not contain db/dbPostgreSql.");
	}

	private static void GrantSystemAdministratorRolePostgres(string connectionString, Guid userId) {
		using NpgsqlConnection connection = new(connectionString);
		connection.Open();
		Guid roleId = ReadPostgresSystemAdministratorsRoleId(connection);
		using NpgsqlCommand command = new("""
			insert into "SysUserInRole" ("Id", "CreatedOn", "ModifiedOn", "SysUserId", "SysRoleId", "ProcessListeners")
			select @id, now(), now(), @userId, @roleId, 0
			where not exists (
				select 1 from "SysUserInRole" where "SysUserId" = @userId and "SysRoleId" = @roleId
			);

			insert into "SysAdminUnitInRole" ("Id", "CreatedOn", "ModifiedOn", "SysAdminUnitId", "SysAdminUnitRoleId", "ProcessListeners", "Source")
			select @adminUnitRoleId, now(), now(), @userId, @roleId, 0, 2
			where not exists (
				select 1 from "SysAdminUnitInRole" where "SysAdminUnitId" = @userId and "SysAdminUnitRoleId" = @roleId
			);
			""", connection);
		command.Parameters.AddWithValue("id", Guid.NewGuid());
		command.Parameters.AddWithValue("adminUnitRoleId", Guid.NewGuid());
		command.Parameters.AddWithValue("userId", userId);
		command.Parameters.AddWithValue("roleId", roleId);
		command.ExecuteNonQuery();
	}

	private static Guid ReadPostgresSystemAdministratorsRoleId(NpgsqlConnection connection) {
		using NpgsqlCommand command = new(
			"""select "Id" from "SysAdminUnit" where "Name" = @name limit 1;""",
			connection);
		command.Parameters.AddWithValue("name", SystemAdministratorsRoleName);
		object result = command.ExecuteScalar()
			?? throw new InvalidOperationException($"Role '{SystemAdministratorsRoleName}' was not found.");
		return (Guid)result;
	}

	private static void GrantSystemAdministratorRoleSqlServer(string connectionString, Guid userId) {
		using SqlConnection connection = new(connectionString);
		connection.Open();
		Guid roleId = ReadSqlServerSystemAdministratorsRoleId(connection);
		using SqlCommand command = new("""
			insert into [SysUserInRole] ([Id], [CreatedOn], [ModifiedOn], [SysUserId], [SysRoleId], [ProcessListeners])
			select @id, sysutcdatetime(), sysutcdatetime(), @userId, @roleId, 0
			where not exists (
				select 1 from [SysUserInRole] where [SysUserId] = @userId and [SysRoleId] = @roleId
			);

			insert into [SysAdminUnitInRole] ([Id], [CreatedOn], [ModifiedOn], [SysAdminUnitId], [SysAdminUnitRoleId], [ProcessListeners], [Source])
			select @adminUnitRoleId, sysutcdatetime(), sysutcdatetime(), @userId, @roleId, 0, 2
			where not exists (
				select 1 from [SysAdminUnitInRole] where [SysAdminUnitId] = @userId and [SysAdminUnitRoleId] = @roleId
			);
			""", connection);
		command.Parameters.AddWithValue("@id", Guid.NewGuid());
		command.Parameters.AddWithValue("@adminUnitRoleId", Guid.NewGuid());
		command.Parameters.AddWithValue("@userId", userId);
		command.Parameters.AddWithValue("@roleId", roleId);
		command.ExecuteNonQuery();
	}

	private static Guid ReadSqlServerSystemAdministratorsRoleId(SqlConnection connection) {
		using SqlCommand command = new(
			"select top 1 [Id] from [SysAdminUnit] where [Name] = @name;",
			connection);
		command.Parameters.AddWithValue("@name", SystemAdministratorsRoleName);
		object result = command.ExecuteScalar()
			?? throw new InvalidOperationException($"Role '{SystemAdministratorsRoleName}' was not found.");
		return (Guid)result;
	}
}

/// <inheritdoc />
public sealed class IdentityServiceSystemUserResolver : IIdentityServiceSystemUserResolver
{
	/// <inheritdoc />
	public string ResolveSystemUserId(EnvironmentSettings environment, string systemUserName) {
		if (string.IsNullOrWhiteSpace(systemUserName)) {
			throw new ArgumentException("System user name is required.", nameof(systemUserName));
		}
		string connectionString = IdentityServiceDeploymentService.ReadCreatioDbConnectionString(environment);
		Guid userId = IdentityServiceDeploymentService.IsPostgres(connectionString)
			? ResolvePostgresSystemUserId(connectionString, systemUserName)
			: ResolveSqlServerSystemUserId(connectionString, systemUserName);
		return userId.ToString();
	}

	private static Guid ResolvePostgresSystemUserId(string connectionString, string systemUserName) {
		using NpgsqlConnection connection = new(connectionString);
		connection.Open();
		using NpgsqlCommand command = new(
			"""select "Id" from "SysAdminUnit" where "Name" = @name limit 1;""",
			connection);
		command.Parameters.AddWithValue("name", systemUserName);
		object result = command.ExecuteScalar()
			?? throw new InvalidOperationException($"Creatio system user '{systemUserName}' was not found.");
		return (Guid)result;
	}

	private static Guid ResolveSqlServerSystemUserId(string connectionString, string systemUserName) {
		using SqlConnection connection = new(connectionString);
		connection.Open();
		using SqlCommand command = new(
			"select top 1 [Id] from [SysAdminUnit] where [Name] = @name;",
			connection);
		command.Parameters.AddWithValue("@name", systemUserName);
		object result = command.ExecuteScalar()
			?? throw new InvalidOperationException($"Creatio system user '{systemUserName}' was not found.");
		return (Guid)result;
	}
}

/// <inheritdoc />
public sealed class IdentityServiceDeploymentService : IIdentityServiceDeploymentService
{
	private const int DefaultIdentityPortRangeEnd = 40100;
	private const int DefaultIdentityPortRangeStart = 40001;
	private const string DesignerClientId = "bpmonline-designer";
	private const string DefaultSystemUser = "Supervisor";
	private static readonly TimeSpan SiteNameRegexTimeout = TimeSpan.FromSeconds(1);
	private readonly IIdentityServiceArchiveResolver _archiveResolver;
	private readonly IAvailableIisPortService _availableIisPortService;
	private readonly IIdentityServiceCreatioClient _creatioClient;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly IIdentityServiceRoleGrantService _roleGrantService;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IIdentityServiceSystemUserResolver _systemUserResolver;
	private readonly ISysSettingsManager _sysSettingsManager;

	/// <summary>
	/// Initializes a new instance of the <see cref="IdentityServiceDeploymentService"/> class.
	/// </summary>
	public IdentityServiceDeploymentService(
		ISettingsRepository settingsRepository,
		IIdentityServiceArchiveResolver archiveResolver,
		IIdentityServiceCreatioClient creatioClient,
		IHttpClientFactory httpClientFactory,
		ISysSettingsManager sysSettingsManager,
		IProcessExecutor processExecutor,
		IAvailableIisPortService availableIisPortService,
		IIdentityServiceRoleGrantService roleGrantService,
		IIdentityServiceSystemUserResolver systemUserResolver,
		ILogger logger) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_archiveResolver = archiveResolver ?? throw new ArgumentNullException(nameof(archiveResolver));
		_creatioClient = creatioClient ?? throw new ArgumentNullException(nameof(creatioClient));
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_sysSettingsManager = sysSettingsManager ?? throw new ArgumentNullException(nameof(sysSettingsManager));
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_availableIisPortService = availableIisPortService
			?? throw new ArgumentNullException(nameof(availableIisPortService));
		_roleGrantService = roleGrantService ?? throw new ArgumentNullException(nameof(roleGrantService));
		_systemUserResolver = systemUserResolver ?? throw new ArgumentNullException(nameof(systemUserResolver));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc />
	public IdentityServiceDeploymentResult Deploy(DeployIdentityOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ValidateOptions(options);

		string environmentName = _settingsRepository.GetActualEnvironmentName(options.Environment)
			?? options.Environment
			?? _settingsRepository.GetDefaultEnvironmentName();
		EnvironmentSettings environment = _settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException($"Environment '{environmentName}' is not registered.");
		string siteName = string.IsNullOrWhiteSpace(options.IdentitySiteName)
			? $"{environmentName}-identity"
			: options.IdentitySiteName;
		ValidateSiteName(siteName);
		string identityPath = ResolveIdentityPath(options, environment, siteName);
		ValidateIdentityPath(identityPath);
		int identitySitePort = ResolveIdentitySitePort(options);
		string identityUrl = $"http://localhost:{identitySitePort}";
		string identityArchivePathInBundle = NormalizeIdentityArchivePathInBundle(options.IdentityArchivePathInBundle);

		string zipFile = ResolveZipFile(options, environment, identityArchivePathInBundle);
		string standaloneArchive = _archiveResolver.Resolve(zipFile, identityArchivePathInBundle);
		ExtractIdentityService(standaloneArchive, identityPath, options.Overwrite);
		ConfigureAppSettings(identityPath, environment);
		GenerateCertificateIfScriptExists(identityPath);
		CreateIisSite(identityPath, siteName, identitySitePort);
		VerifyIdentityDiscovery(identityUrl);

		string designerSecret = _creatioClient.GetDesignerClientSecret();
		if (string.IsNullOrWhiteSpace(designerSecret)) {
			designerSecret = ReadBundledDesignerClientSecret(identityPath);
		}
		ConfigureCreatioConnection(options.ConfigurationMode, identityUrl, designerSecret);
		if (options.NoApp) {
			return new IdentityServiceDeploymentResult(
				true,
				"IdentityService deployed and connected to Creatio. OAuth app creation skipped by --no-app; no clio client credentials were persisted and token verification was skipped.",
				identityUrl,
				string.Empty);
		}
		string systemUserName = string.IsNullOrWhiteSpace(options.SystemUser) ? DefaultSystemUser : options.SystemUser;
		string systemUserId;
		if (options.CreateTechUser) {
			systemUserId = _creatioClient.CreateTechnicalUser(systemUserName);
			_roleGrantService.GrantSystemAdministratorRole(environment, systemUserId);
		} else {
			systemUserId = _systemUserResolver.ResolveSystemUserId(environment, systemUserName);
		}
		OAuthClientCredentials credentials = _creatioClient.CreateClioClient(options, systemUserId);
		VerifyClientCredentials(identityUrl, credentials);
		PersistClioEnvironment(environmentName, environment, identityUrl, credentials);

		return new IdentityServiceDeploymentResult(
			true,
			"IdentityService deployed and clio OAuth settings updated.",
			identityUrl,
			credentials.ClientId);
	}

	private static void ValidateOptions(DeployIdentityOptions options) {
		if (options.NoApp && options.CreateTechUser) {
			throw new ArgumentException("--no-app cannot be combined with --create-tech-user because no OAuth app is created.");
		}
		if (options.NoApp && !string.IsNullOrWhiteSpace(options.SystemUser)) {
			throw new ArgumentException("--no-app cannot be combined with --user because no OAuth app is created.");
		}
		if (options.IdentitySitePort.HasValue
			&& options.IdentitySitePort.Value is <= 0 or > 65535) {
			throw new ArgumentOutOfRangeException(nameof(options.IdentitySitePort), "Port must be in range 1-65535.");
		}
		string mode = NormalizeMode(options.ConfigurationMode);
		if (mode == "db") {
			throw new NotSupportedException(
				"Direct DB configuration is not implemented yet. Use --configuration-mode rest or db-first.");
		}
	}

	private static void ValidateSiteName(string siteName) {
		if (string.IsNullOrWhiteSpace(siteName)
			|| !Regex.IsMatch(siteName, @"^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant, SiteNameRegexTimeout)) {
			throw new ArgumentException(
				"IdentityService site name may contain only letters, digits, dot, underscore, and hyphen.",
				nameof(siteName));
		}
	}

	private static void ValidateIdentityPath(string identityPath) {
		if (identityPath.Contains('"')) {
			throw new ArgumentException("IdentityService path must not contain quote characters.", nameof(identityPath));
		}
	}

	private static string NormalizeMode(string mode) =>
		string.IsNullOrWhiteSpace(mode) ? "db-first" : mode.Trim().ToLowerInvariant();

	private static string NormalizeIdentityArchivePathInBundle(string identityArchivePathInBundle) =>
		string.IsNullOrWhiteSpace(identityArchivePathInBundle) ? "IdentityService.zip" : identityArchivePathInBundle;

	private static string ResolveZipFile(
		DeployIdentityOptions options,
		EnvironmentSettings environment,
		string identityArchivePathInBundle) {
		if (!string.IsNullOrWhiteSpace(options.ZipFile)) {
			return options.ZipFile;
		}
		if (string.IsNullOrWhiteSpace(environment.EnvironmentPath) || !Directory.Exists(environment.EnvironmentPath)) {
			throw new InvalidOperationException(
				"The target environment must have an existing EnvironmentPath when --zip-file is omitted.");
		}
		string nestedPath = identityArchivePathInBundle.Replace('/', Path.DirectorySeparatorChar)
			.Replace('\\', Path.DirectorySeparatorChar);
		string exactPath = Path.Combine(environment.EnvironmentPath, nestedPath);
		if (File.Exists(exactPath)) {
			return exactPath;
		}
		string fileName = Path.GetFileName(nestedPath);
		string discoveredPath = Directory
			.EnumerateFiles(environment.EnvironmentPath, fileName, SearchOption.AllDirectories)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(discoveredPath)) {
			return discoveredPath;
		}
		throw new InvalidOperationException(
			$"No '{fileName}' was found under EnvironmentPath '{environment.EnvironmentPath}'. Pass --zip-file.");
	}

	private int ResolveIdentitySitePort(DeployIdentityOptions options) {
		if (options.IdentitySitePort.HasValue) {
			return options.IdentitySitePort.Value;
		}
		FindAvailableIisPortResult result = _availableIisPortService
			.FindAsync(DefaultIdentityPortRangeStart, DefaultIdentityPortRangeEnd)
			.GetAwaiter()
			.GetResult();
		if (result.FirstAvailablePort.HasValue) {
			return result.FirstAvailablePort.Value;
		}
		throw new InvalidOperationException(
			$"No free IIS deployment port was found between {DefaultIdentityPortRangeStart} and {DefaultIdentityPortRangeEnd}. {result.Summary}");
	}

	private static string ResolveIdentityPath(DeployIdentityOptions options, EnvironmentSettings environment, string siteName) {
		if (!string.IsNullOrWhiteSpace(options.IdentityPath)) {
			return Path.GetFullPath(options.IdentityPath);
		}
		if (!string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
			string parent = Directory.GetParent(environment.EnvironmentPath)?.FullName
				?? environment.EnvironmentPath;
			return Path.Combine(parent, siteName);
		}
		return Path.Combine(Environment.CurrentDirectory, siteName);
	}

	private static void ExtractIdentityService(string archivePath, string identityPath, bool overwrite) {
		if (Directory.Exists(identityPath)) {
			if (!overwrite) {
				throw new InvalidOperationException(
					$"IdentityService target '{identityPath}' already exists. Pass --overwrite to replace files.");
			}
			Directory.Delete(identityPath, recursive: true);
		}
		Directory.CreateDirectory(identityPath);
		ZipFile.ExtractToDirectory(archivePath, identityPath, overwriteFiles: true);
	}

	private static void ConfigureAppSettings(string identityPath, EnvironmentSettings environment) {
		string appSettingsPath = Path.Combine(identityPath, "appsettings.json");
		if (!File.Exists(appSettingsPath)) {
			throw new FileNotFoundException("IdentityService appsettings.json was not found after extraction.", appSettingsPath);
		}
		string connectionString = ReadCreatioDbConnectionString(environment);
		JsonNode root = JsonNode.Parse(File.ReadAllText(appSettingsPath))
			?? throw new InvalidOperationException("IdentityService appsettings.json is empty.");
		root["DbProvider"] = IsPostgres(connectionString) ? "Postgres" : "MsSql";
		root["DatabaseConnectionString"] = connectionString;
		root["X509CertificatePath"] = @".\pfx\win\certificateDirectory\openssl.pfx";
		File.WriteAllText(appSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}

	internal static bool IsPostgres(string connectionString) =>
		connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
		|| (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
			&& connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase)
			&& !connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase));

	internal static string ReadCreatioDbConnectionString(EnvironmentSettings environment) {
		if (string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
			throw new InvalidOperationException("The target environment does not have EnvironmentPath configured.");
		}
		string connectionStringsPath = Path.Combine(environment.EnvironmentPath, "ConnectionStrings.config");
		if (!File.Exists(connectionStringsPath)) {
			throw new FileNotFoundException("Creatio ConnectionStrings.config was not found.", connectionStringsPath);
		}
		XDocument document = XDocument.Load(connectionStringsPath);
		XElement element = document.Root?.Elements("add")
			.FirstOrDefault(item => string.Equals(item.Attribute("name")?.Value, "dbPostgreSql", StringComparison.OrdinalIgnoreCase))
			?? document.Root?.Elements("add")
				.FirstOrDefault(item => string.Equals(item.Attribute("name")?.Value, "db", StringComparison.OrdinalIgnoreCase));
		return element?.Attribute("connectionString")?.Value
			?? throw new InvalidOperationException("ConnectionStrings.config does not contain db/dbPostgreSql.");
	}

	private static string ReadBundledDesignerClientSecret(string identityPath) {
		string appSettingsPath = Path.Combine(identityPath, "appsettings.json");
		JsonNode root = JsonNode.Parse(File.ReadAllText(appSettingsPath))
			?? throw new InvalidOperationException("IdentityService appsettings.json is empty.");
		string clientsJson = root["Clients"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(clientsJson)) {
			throw new InvalidOperationException("IdentityService appsettings.json does not contain Clients.");
		}
		using JsonDocument clients = JsonDocument.Parse(clientsJson);
		foreach (JsonElement client in clients.RootElement.EnumerateArray()) {
			string clientId = client.TryGetProperty("ClientId", out JsonElement idElement)
				? idElement.GetString()
				: string.Empty;
			if (!string.Equals(clientId, DesignerClientId, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (client.TryGetProperty("Secrets", out JsonElement secretsElement)
				&& secretsElement.ValueKind == JsonValueKind.Array) {
				string secret = secretsElement.EnumerateArray()
					.Select(item => item.GetString())
					.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
				if (!string.IsNullOrWhiteSpace(secret)) {
					return secret;
				}
			}
		}
		throw new InvalidOperationException(
			$"IdentityService appsettings.json does not contain a secret for '{DesignerClientId}'.");
	}

	private void GenerateCertificateIfScriptExists(string identityPath) {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return;
		}
		string scriptPath = Path.Combine(identityPath, "pfx", "win", "generate_pfx.ps1");
		if (!File.Exists(scriptPath)) {
			_logger.WriteWarning("IdentityService PFX generation script was not found; skipping certificate generation.");
			return;
		}
		string certificateDirectory = Path.Combine(identityPath, "pfx", "win", "certificateDirectory");
		_processExecutor.Execute(
			"powershell.exe",
			$"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -outputPath \"{certificateDirectory}\"",
			waitForExit: true,
			workingDirectory: Path.GetDirectoryName(scriptPath));
		string certificatePath = Path.Combine(certificateDirectory, "openssl.pfx");
		ValidateCertificate(certificatePath);
	}

	private static void ValidateCertificate(string certificatePath) {
		if (!File.Exists(certificatePath)) {
			throw new FileNotFoundException("IdentityService certificate was not generated.", certificatePath);
		}
		using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
			certificatePath,
			password: null);
		if (certificate.NotAfter <= DateTime.Now) {
			throw new InvalidOperationException(
				$"IdentityService certificate '{certificatePath}' is expired: {certificate.NotAfter:O}.");
		}
	}

	private void CreateIisSite(string identityPath, string siteName, int port) {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			throw new PlatformNotSupportedException("deploy-identity currently supports IIS deployment on Windows only.");
		}
		string appcmdPath = Path.Combine("C:", "Windows", "System32", "inetsrv", "appcmd.exe");
		if (!AppCmdSucceeds(appcmdPath, $"list apppool /name:\"{siteName}\"")) {
			RunAppCmd(appcmdPath,
				$"add apppool /name:\"{siteName}\" /managedRuntimeVersion:\"\" /managedPipelineMode:\"Integrated\"");
		}
		RunAppCmd(appcmdPath, $"set apppool \"{siteName}\" /processModel.loadUserProfile:true");
		if (!AppCmdSucceeds(appcmdPath, $"list site /name:\"{siteName}\"")) {
			RunAppCmd(appcmdPath,
				$"add site /name:\"{siteName}\" /bindings:\"http/*:{port}:\" /physicalPath:\"{identityPath}\" /applicationDefaults.applicationPool:\"{siteName}\"");
		}
		RunAppCmd(appcmdPath, $"start apppool /apppool.name:\"{siteName}\"");
		RunAppCmd(appcmdPath, $"start site /site.name:\"{siteName}\"");
	}

	private bool AppCmdSucceeds(string appcmdPath, string arguments) {
		ProcessExecutionResult result = Task.Run(() =>
				_processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions(appcmdPath, arguments)))
			.GetAwaiter()
			.GetResult();
		return result.ExitCode == 0;
	}

	private void RunAppCmd(string appcmdPath, string arguments) {
		ProcessExecutionResult result = Task.Run(() =>
				_processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions(appcmdPath, arguments)))
			.GetAwaiter()
			.GetResult();
		if (result.ExitCode != 0) {
			throw new InvalidOperationException(
				$"appcmd failed with exit code {result.ExitCode}: {result.StandardError}{result.StandardOutput}");
		}
	}

	private void VerifyIdentityDiscovery(string identityUrl) {
		HttpClient client = _httpClientFactory.CreateClient();
		HttpResponseMessage response = Task.Run(() =>
				client.GetAsync($"{identityUrl.TrimEnd('/')}/.well-known/openid-configuration"))
			.GetAwaiter()
			.GetResult();
		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException(
				$"IdentityService discovery check failed with HTTP {(int)response.StatusCode}.");
		}
	}

	private void ConfigureCreatioConnection(string mode, string identityUrl, string designerSecret) {
		string normalizedMode = NormalizeMode(mode);
		if (normalizedMode == "db-first") {
			_logger.WriteWarning("DB-first IdentityService configuration is not proven yet; falling back to REST/sys-settings.");
		}
		if (normalizedMode is not "db-first" and not "rest") {
			throw new NotSupportedException($"Unsupported configuration mode '{mode}'.");
		}
		bool urlUpdated = _sysSettingsManager.UpdateSysSetting("OAuth20IdentityServerUrl", identityUrl, "Text");
		bool clientUpdated = _sysSettingsManager.UpdateSysSetting("OAuth20IdentityServerClientId", DesignerClientId, "Text");
		bool secretUpdated = _sysSettingsManager.UpdateSysSetting(
			"OAuth20IdentityServerClientSecret",
			designerSecret,
			"SecureText");
		if (!urlUpdated || !clientUpdated || !secretUpdated) {
			throw new InvalidOperationException("Failed to update one or more IdentityService system settings.");
		}
	}

	private void VerifyClientCredentials(string identityUrl, OAuthClientCredentials credentials) {
		HttpClient client = _httpClientFactory.CreateClient();
		using FormUrlEncodedContent content = new(new Dictionary<string, string> {
			["grant_type"] = "client_credentials",
			["client_id"] = credentials.ClientId,
			["client_secret"] = credentials.ClientSecret
		});
		content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
		HttpResponseMessage response = Task.Run(() =>
				client.PostAsync($"{identityUrl.TrimEnd('/')}/connect/token", content))
			.GetAwaiter()
			.GetResult();
		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException(
				$"IdentityService client_credentials token check failed with HTTP {(int)response.StatusCode}.");
		}
	}

	private void PersistClioEnvironment(
		string environmentName,
		EnvironmentSettings environment,
		string identityUrl,
		OAuthClientCredentials credentials) {
		EnvironmentSettings updated = new() {
			Uri = environment.Uri,
			Login = environment.Login,
			Password = environment.Password,
			Maintainer = environment.Maintainer,
			IsNetCore = environment.IsNetCore,
			ClientId = credentials.ClientId,
			ClientSecret = credentials.ClientSecret,
			AuthAppUri = $"{identityUrl.TrimEnd('/')}/connect/token",
			WorkspacePathes = environment.WorkspacePathes,
			EnvironmentPath = environment.EnvironmentPath,
			DbName = environment.DbName,
			DbServerKey = environment.DbServerKey,
			BackupFilePath = environment.BackupFilePath,
			Safe = environment.Safe,
			DeveloperModeEnabled = environment.DeveloperModeEnabled
		};
		_settingsRepository.ConfigureEnvironment(environmentName, updated);
	}

	internal static string ExtractFirstString(string json, params string[] propertyNames) {
		if (string.IsNullOrWhiteSpace(json)) {
			return string.Empty;
		}
		using JsonDocument document = JsonDocument.Parse(json);
		foreach (string propertyName in propertyNames) {
			if (TryFindString(document.RootElement, propertyName, out string value)) {
				return value;
			}
		}
		return string.Empty;
	}

	private static bool TryFindString(JsonElement element, string propertyName, out string value) {
		if (element.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in element.EnumerateObject()) {
				if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
					&& property.Value.ValueKind == JsonValueKind.String) {
					value = property.Value.GetString();
					return true;
				}
				if (TryFindString(property.Value, propertyName, out value)) {
					return true;
				}
			}
		}
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryFindString(item, propertyName, out value)) {
					return true;
				}
			}
		}
		value = string.Empty;
		return false;
	}
}
