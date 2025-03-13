using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;
using clioAgent.ChainOfResponsibility;
using clioAgent.DbOperations;
using clioAgent.Providers;
using ErrorOr;

namespace clioAgent.Handlers.ChainLinks;

[Traceabale]
public class ResorePgLink(IPostgres postgres, Settings settings) : AbstractChainLink<RequestContext, ResponseContext> {

	protected override Dictionary<string, object> Tags { get; } = new (){
		{ "HandlerName", "ResorePg Link"}
	};
	private IInitializedPostgres _pg;
	private IFileInfo _backupFile;
	protected override bool ShouldContinue(RequestContext request, ResponseContext response) => 
		!response.Result.IsError;
	protected override bool ShouldProcess(RequestContext request){
		if(!postgres.IsInitialized) {
			var p = postgres.Init();
			if(p.IsError) {
				return false;
			}else {
				_pg = p.Value;
			}
		}
		_pg = postgres as IInitializedPostgres;
		
		if(request.UnzippedDirectory is null || !request.UnzippedDirectory.Exists) {
			return false;
		}
		IFileInfo[] files =  request
							.UnzippedDirectory
							.GetFiles($"db/*{WorkingDirectoryProvider.PgBackupExtension}", SearchOption.AllDirectories);
		
		if(files.Length == 0) {
			return false;
		}
		_backupFile = files[0];
		return !string.IsNullOrWhiteSpace(request.DbName);
	}

	protected override async Task<ResponseContext> ProcessAsync(RequestContext request) => 
		(await CreatePgDatabaseAsync(request.TemplateName, _pg.DbSettings!, request.ZipFile!))
			.Match<ResponseContext>(
				onValue: success => new ResponseContext {
					Result = Result.Success,
				},
				onError: err => new ResponseContext {
					Result = err
				});

	/// <summary>
	///  Restores the PostgresSQL database from a backup file.
	/// </summary>
	/// <param name="templateDbName">The name of the database to be restored.</param>
	/// <param name="dbSettings">Database settings from connectionstring</param>
	/// <param name="zipFile"></param>
	/// <returns>
	///  An <see cref="ErrorOr{T}" /> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	///  If the restore process fails, an error is returned with the exception details.
	/// </remarks>
	private async Task<ErrorOr<Success>> CreatePgDatabaseAsync(string templateDbName, DbSettings dbSettings, IFileSystemInfo zipFile){
		
		string zipFileWithoutExtension = Path.GetFileNameWithoutExtension(zipFile.FullName);
		string? binPath = settings.Db?
			.FirstOrDefault(d=> d.Type == "PGSQL")
			?.Servers?.FirstOrDefault()?.BinFolderPath;
		
		if (string.IsNullOrWhiteSpace(binPath)) {
			return Error.Failure("RestoreDb.BinPathNotFound", "Bin path not found");
		}


		ErrorOr<ProcessStartInfo> maybeStartInfo = CreateProcessStartInfo(dbSettings, templateDbName, binPath, _backupFile);
		if (maybeStartInfo.IsError) {
			return maybeStartInfo.Errors;
		}
		ProcessStartInfo startInfo = maybeStartInfo.Value;
		ErrorOr<(List<string> output, List<string> errors)> restoreDbResult = ExecuteProcess(startInfo);

		await using StreamWriter writer = new(@"C:\restoredb_log.txt");
		StringBuilder sb = new();
		restoreDbResult.Value.errors.ForEach(e => sb.AppendLine(e));
		await writer.WriteAsync(sb.ToString());
		await _pg.SetDatabaseAsTemplateAsync(templateDbName, zipFileWithoutExtension);
		return restoreDbResult.IsError ? restoreDbResult.Errors : Result.Success;
		
	}

	private static ErrorOr<ProcessStartInfo> CreateProcessStartInfo(DbSettings dbSettings, string templateDbName,
		string binPath, IFileSystemInfo backupFile){
		ProcessStartInfo startInfo = new() {
			FileName = Path.Combine(binPath, "pg_restore.exe"),
			ArgumentList = {
				$"--dbname={templateDbName}",
				"--verbose",
				"--no-owner",
				"--no-privileges",
				"--jobs=4",
				$"{backupFile.FullName}"
			},
			UseShellExecute = false,
			WorkingDirectory = binPath,
			EnvironmentVariables = {
				{"PGPASSWORD", $"{dbSettings.Password ?? string.Empty}"}
			},
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		if (!string.IsNullOrWhiteSpace(dbSettings.UserId)) {
			string usernameSetting = $"--username={dbSettings.UserId}";
			startInfo.ArgumentList.Add(usernameSetting);
		}
		if (!string.IsNullOrWhiteSpace(dbSettings.Server)) {
			string hostSetting = $"--host={dbSettings.Server}";
			startInfo.ArgumentList.Add(hostSetting);
		}
		if (dbSettings.Port != 0) {
			string portSetting = $"--port={dbSettings.Port}";
			startInfo.ArgumentList.Add(portSetting);
		}
		return startInfo;
	}

	private static ErrorOr<(List<string> output, List<string> errors)> ExecuteProcess(ProcessStartInfo startInfo){
		List<string> output = [];
		List<string> errors = [];
		using Process? process = Process.Start(startInfo);

		if (process == null) {
			return Error.Failure("RestoreDb.RestoreFailed", "Failed to start pg_restore process");
		}
		process.OutputDataReceived += (_, args) => {
			if (args.Data != null) {
				output.Add(args.Data);
			}
		};
		process.ErrorDataReceived += (_, args) => {
			if (args.Data != null) {
				errors.Add(args.Data);
			}
		};
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		//pg_restore.exe writes its verbose output to stderr
		return (output, errors);
	}
}
