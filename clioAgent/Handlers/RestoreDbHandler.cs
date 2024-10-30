using System.Data.Common;
using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using clioAgent.DbOperations;
using ErrorOr;

namespace clioAgent.Handlers;

public sealed class RestoreDbHandler(Settings settings, IFileSystem fileSystem) : BaseHandler {
	#region Methods: Private

	/// <summary>
	///  Copies a file to the working directory.
	/// </summary>
	/// <param name="from">The path of the file to be copied.</param>
	/// <returns>
	///  An <see cref="ErrorOr{IFileInfo}" /> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	///  If the file does not exist after copying, an error is returned.
	/// </remarks>
	private ErrorOr<IFileInfo> CopyFileToWorkingDirectory(string from){
		if (string.IsNullOrEmpty(from)) {
			return Error.Validation("CopyFileToWorkingDirectory.InvalidPath",
				"Invalid path, value cannot be null or empty");
		}

		string fileName = Path.GetFileName(from);
		string to = Path.Combine(settings.WorkingDirectoryPath, fileName);
		try {
			if (!fileSystem.Directory.Exists(settings.WorkingDirectoryPath)) {
				fileSystem.Directory.CreateDirectory(settings.WorkingDirectoryPath);
			}

			IFileInfo fromFileInfo = fileSystem.FileInfo.New(from);
			if (!fromFileInfo.Exists) {
				return Error.Failure("CopyFileToWorkingDirectory.FileNotFound",
					$"File {fromFileInfo.FullName} not found");
			}

			IFileInfo toFileInfo = fileSystem.FileInfo.New(to);
			if (toFileInfo.Exists) {
				toFileInfo.Delete();
			}
			
			fileSystem.File.Copy(fromFileInfo.FullName, toFileInfo.FullName);
			IFileInfoFactory fiFactory = fileSystem.FileInfo;
			IFileInfo fileInfo = fiFactory.New(to);
			
			return fileInfo.Exists
				? fileInfo.ToErrorOr()
				: Error.Failure("File not found", "File not found");
		} catch (Exception e) {
			Error error = Error.Failure("CopyFileToWorkingDirectory.Exception", e.Message);
			return error;
		}
	}

	protected override ErrorOr<Success> InternalExecute(Dictionary<string, object> commandObj, CancellationToken cancellationToken){
		commandObj.TryGetValue("CreatioBuildZipPath", out object? creatioBuildZipPathObj);
		commandObj.TryGetValue("Name", out object? nameObj);
		if (creatioBuildZipPathObj == null || string.IsNullOrEmpty(creatioBuildZipPathObj.ToString())) {
			Error error = Error.Failure("RestoreDbHandler.CreatioBuildZipPathIsNull", "CreatioBuildZipPath is null");
			return error;
		}
		string creatioBuildZipPath = creatioBuildZipPathObj.ToString()!;

		if (nameObj == null || string.IsNullOrEmpty(nameObj.ToString())) {
			Error error = Error.Failure("RestoreDbHandler.NameIsNull", "Name is null");
			return error;
		}
		string name = nameObj.ToString()!;

		// 1. CopyFile
		ErrorOr<IFileInfo> fileInfo = ExecuteWithTrace(
			()=> CopyFileToWorkingDirectory(creatioBuildZipPath), null, nameof(CopyFileToWorkingDirectory));
		if (fileInfo.IsError) {
			return fileInfo.Errors;
		}

		//2. Unzip
		ErrorOr<IDirectoryInfo> unzipResult = ExecuteWithTrace(() => UnzipFile(fileInfo.Value),null, nameof(UnzipFile));
		if (unzipResult.IsError) {
			return unzipResult.Errors;
		}

		//3. CreateDb
		ErrorOr<Success> newDb =  ExecuteWithTrace(() => CreatePgDatabase(name), null, nameof(CreatePgDatabase));
		if (newDb.IsError) {
			return newDb.Errors;
		}

		//4. RestoreDb
		ErrorOr<(List<string> output, List<string> errors)> restoreDbResult = ExecuteWithTrace(() =>LaunchPgRestoreProcess(name, unzipResult.Value), null, nameof(LaunchPgRestoreProcess));
		
		using StreamWriter writer = new (@"C:\restoredb_log.txt");
		StringBuilder sb = new ();
		restoreDbResult.Value.errors.ForEach(e => sb.AppendLine(e));
		writer.Write(sb.ToString());
		
		return restoreDbResult.IsError ? restoreDbResult.Errors : Result.Success;
	}

	/// <summary>
	/// Creates a new PostgresSQL database.
	/// </summary>
	/// <param name="dbName">The name of the database to be created.</param>
	/// <returns>
	/// An <see cref="ErrorOr{Success}" /> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	/// If the database already exists, it will be dropped and recreated.
	/// </remarks>
	private ErrorOr<Success> CreatePgDatabase(string dbName){
		Db? dbSection = settings.Db?.FirstOrDefault(db => db.Type == "PGSQL");
		if (dbSection is null) {
			return Error.Failure("RestoreDb.DbSettingsNotFound", "Db settings not found");
		}

		string cs = dbSection.Servers?.FirstOrDefault()?.ConnectionString ?? string.Empty;
		if (string.IsNullOrEmpty(cs)) {
			return Error.Failure("RestoreDb.ConnectionStringNotFound", "Connection string not found");
		}

		Postgres postgres = new(cs);

		if (postgres.CheckDbExists(dbName)) {
			postgres.DropDb(dbName);
		}

		bool isDbCreated = postgres.CreateDb(dbName);
		if (!isDbCreated) {
			return Error.Failure("RestoreDb.DbCreationFailed", "Database creation failed");
		}
		return Result.Success;
	}

	/// <summary>
	/// Restores the PostgresSQL database from a backup file.
	/// </summary>
	/// <param name="dbName">The name of the database to be restored.</param>
	/// <param name="unzippedArchiveFolder">The directory containing the unzipped backup files.</param>
	/// <returns>
	/// An <see cref="ErrorOr{T}"/> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	/// If the restore process fails, an error is returned with the exception details.
	/// </remarks>
	private ErrorOr<(List<string> output, List<string> errors)> LaunchPgRestoreProcess(string dbName, IDirectoryInfo unzippedArchiveFolder){
		Db? dbSection = settings.Db?.FirstOrDefault(db => db.Type == "PGSQL");
		if (dbSection is null) {
			return Error.Failure("RestoreDb.DbSettingsNotFound", "Db settings not found");
		}
		string cs = dbSection.Servers?.FirstOrDefault()?.ConnectionString ?? string.Empty;
		if (string.IsNullOrEmpty(cs)) {
			return Error.Failure("RestoreDb.ConnectionStringNotFound", "Connection string not found");
		}

		DbConnectionStringBuilder builder = new() {ConnectionString = cs};
		builder.TryGetValue("Server", out object? csServerObj);
		builder.TryGetValue("Port", out object? csPortObj);
		builder.TryGetValue("password", out object? csPasswordObj);
		builder.TryGetValue("User ID", out object? csUserIdObj);

		string binPath = dbSection.Servers?.FirstOrDefault()?.BinFolderPath ?? string.Empty;
		if (string.IsNullOrEmpty(binPath)) {
			return Error.Failure("RestoreDb.BinPathNotFound", "Bin path not found");
		}

		IFileInfo[] backupFiles = unzippedArchiveFolder.GetFiles("*.backup", SearchOption.AllDirectories);
		if (backupFiles.Length == 0) {
			return Error.Failure("RestoreDb.BackupNotFound", "No backup file found");
		}

		ProcessStartInfo startInfo = new() {
			FileName = Path.Combine(binPath, "pg_restore.exe"),
			ArgumentList = {
				$"--dbname={dbName}",
				"--verbose",
				"--no-owner",
				"--no-privileges",
				"--jobs=4",
				$"--username={csUserIdObj}",
				$"--host={csServerObj}",
				$"--port={csPortObj}",
				$"{backupFiles[0].FullName}"
			},
			UseShellExecute = false,
			WorkingDirectory = binPath,
			EnvironmentVariables = {
				{"PGPASSWORD", $"{csPasswordObj}"}
			},
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
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

	/// <summary>
	///  Unzips the specified zip file to a directory within the working directory.
	/// </summary>
	/// <param name="zipFile">The zip file to be extracted.</param>
	/// <returns>
	///  An <see cref="ErrorOr{Success}" /> indicating the result of the operation.
	/// </returns>
	/// <remarks>
	///  If the extraction fails, an error is returned with the exception details.
	/// </remarks>
	private ErrorOr<IDirectoryInfo> UnzipFile(IFileInfo zipFile){
		try {
			string fileExtension = Path.GetExtension(zipFile.Name);
			string fileName = Path.GetFileNameWithoutExtension(zipFile.Name);
			string fileNameWithoutExtension = fileName.Replace(fileExtension, "");
			string extractPath = Path.Combine(settings.WorkingDirectoryPath, fileNameWithoutExtension);

			if (fileSystem.Directory.Exists(extractPath)) {
				fileSystem.Directory.Delete(extractPath, true);
			}
			fileSystem.Directory.CreateDirectory(extractPath);
			ZipFile.ExtractToDirectory(Path.Combine(zipFile.FullName), extractPath);

			IDirectoryInfoFactory diFactory = fileSystem.DirectoryInfo;
			IDirectoryInfo directoryInfo = diFactory.New(extractPath);
			
			return directoryInfo.Exists ? directoryInfo.ToErrorOr()
				: Error.Failure("UnzipFile.DirectoryNotFound", "Directory not found");
			
		} 
		catch (Exception e) {
			return Error.Failure("UnzipFile.Exception", e.Message);
		}
	}

	#endregion
}
