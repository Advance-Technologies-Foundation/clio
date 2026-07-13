namespace Clio.Command.McpServer.Progress;

/// <summary>
/// Stable, kebab-case <c>stageId</c> string constants for every deploy and uninstall stage.
/// </summary>
/// <remarks>
/// The identifiers are <b>string keys</b>, never enum ordinals: string keys keep on-disk
/// receipts comparable across clio versions even when stages are inserted or reordered
/// (ADR D2). They are defined once here so the manifest builder, the stage-event emitters
/// (stories 2 and 3), and the cross-repo contract test all reference the same source of truth.
/// Any change to a value here is a breaking contract change and requires bumping
/// <see cref="ClioStageEventContract.SchemaVersion"/> and updating both repos' fixtures.
/// </remarks>
public static class StageIds {

	/// <summary>Deploy stage: build/prepare the source (network-drive only; conditional).</summary>
	public const string StageBuild = "stage-build";

	/// <summary>Deploy stage: unzip the Creatio distribution.</summary>
	public const string Unzip = "unzip";

	/// <summary>Deploy stage: copy files into the target directory.</summary>
	public const string CopyFiles = "copy-files";

	/// <summary>Deploy stage: restore the application database.</summary>
	public const string RestoreDb = "restore-db";

	/// <summary>Deploy stage: deploy the application (IIS / dotnet host).</summary>
	public const string DeployApp = "deploy-app";

	/// <summary>Deploy stage: configure connection strings (database and Redis).</summary>
	public const string ConfigureConnStrings = "configure-conn-strings";

	/// <summary>Deploy stage: register the environment with clio.</summary>
	public const string RegisterEnv = "register-env";

	/// <summary>Deploy stage: wait until the application reports ready.</summary>
	public const string WaitReady = "wait-ready";

	/// <summary>Uninstall stage: stop the IIS site / application pool.</summary>
	public const string StopIis = "stop-iis";

	/// <summary>Uninstall stage: read the environment configuration.</summary>
	public const string ReadConfig = "read-config";

	/// <summary>Uninstall stage: delete the IIS site / application pool.</summary>
	public const string DeleteIis = "delete-iis";

	/// <summary>Uninstall stage: drop the application database.</summary>
	public const string DropDb = "drop-db";

	/// <summary>Uninstall stage: delete the application files.</summary>
	public const string DeleteFiles = "delete-files";

	/// <summary>Uninstall stage: unregister the environment (final, only after cleanup succeeds).</summary>
	public const string Unregister = "unregister";

	/// <summary>Uninstall stage: delete the application-pool profile (reported skipped/not-supported).</summary>
	public const string DeleteApppoolProfile = "delete-apppool-profile";
}
