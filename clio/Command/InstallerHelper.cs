using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using YamlDotNet.Serialization;

namespace Clio.Command;

public static class InstallerHelper
{

	#region Enum: Public

	public enum DatabaseType
	{

		Postgres,
		MsSql

	}

	public enum FrameworkType
	{

		NetFramework,
		NetCore

	}

	#endregion

	#region Constants: Private

	private const string K8NameSpace = "clio-infrastructure";
	private const string KubectlCommandName = "kubectl";
	private const string PowershellCommandName = "powershell.exe";
	
	private const string MssqlAppName = "clio-mssql";
	private const string MsSqlBackupExtension = ".bak";
	private const string PgBackupExtension = ".backup";
	private const string PostgresAppName = "clio-postgres";
	private const string RootDestinationDirectory = @"D:\Projects\CreatioProductBuild";
	private const string PgConnectionString = "Host=localhost;Port=5432;Username=postgres;Password=root;Database=postgres";
	
	#endregion

	#region Fields: Public
	
	/// <summary>
	/// A Function delegate to create a PostgreSQL database. 
	/// Throws an exception if a stateful set name (database Server) cannot be found.
	/// </summary>
	/// <param name="dbName">The name of the database to be created.</param>
	/// <param name="processExecutor">The process to create the database.</param>
	/// <returns>True if database creation is successful. Throws an exception otherwise.</returns>
	public static readonly Func<string, IProcessExecutor, bool> CreatePostgresDb = (dbName, processExecutor) => {
		return FindStatefulSetName(DatabaseType.Postgres, processExecutor) switch {
			//({ } podName, true) => CreatePostgresDbK8Impl(podName, dbName, processExecutor),
			({ } podName, true) => CreatePostgresDbWithClient(dbName),
			(not null, false) => throw new Exception(
				$"Cannot find stateful set name to create db {dbName}")
		};
	};
	
	public static Func<string, IProcessExecutor, bool> CreateMsSqlDb = (dbName, processExecutor)=> {
		return FindStatefulSetName(DatabaseType.Postgres, processExecutor) switch {
			//({ } podName, true) => CreatePostgresDbK8Impl(podName, dbName, processExecutor),
			({ } podName, true) => CreateMsSqlDbWithClient(dbName),
			(not null, false) => throw new Exception(
				$"Cannot find stateful set name to create db {dbName}")
		};
	};
	
	
	
	public static readonly Func<string, bool> CheckPgTemplateExists = (templateName)=> {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datistemplate = true AND datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PgConnectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			var result = cmd.ExecuteScalar();
			cnn.Close();
			
			return result is long r && r == 1;
			
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	};
	
	public static readonly Func<string, bool> SetPgDatabaseAsTemplate =(dbName)=> {
		try {
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PgConnectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"UPDATE pg_database SET datistemplate='true' WHERE datname='{dbName}'");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	};
	
	public static readonly Func<string, string , bool> CreateDbFromTemplate =(templateName, dbName)=> {
		try {
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PgConnectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" TEMPLATE=\"{templateName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	};
	
	public static Func<string, string, IProcessExecutor, bool> RestorePostgresDbFromBackup = (dbName, fileName, processExecutor) => {
		//podName, dbName, fileName, processExecutor
		return FindStatefulSetName(DatabaseType.Postgres, processExecutor) switch {
			({ } podName, true) => RestorePostgresDbFromBackupK8Impl(podName, dbName, fileName, processExecutor),
			(not null, false) => throw new Exception(
				$"Cannot find stateful set name to create db {dbName}")
		};
	};
	
	
	
	
	/// <summary>
	/// Represents a function that takes a zip file and a package archiver as parameters,
	/// checks if a directory with the same name as the zip file (without the extension) exists.
	/// If it does, it returns the directory info. If not, it unzips the file and returns the info of the new directory.
	/// </summary>
	/// <param name="zipFile">The full path of the zip file.</param>
	/// <param name="packageArchiver">The package archiver instance to be used for the unzipping process.</param>
	/// <returns>The DirectoryInfo object representing the existing or newly created directory.</returns>
	[NotNull]
	public static readonly Func<string, IPackageArchiver, DirectoryInfo> UnzipOrTakeExisting =
		(zipFile, packageArchiver) => {
			string destinationPath = Path.Join(RootDestinationDirectory,
				Path.GetFileNameWithoutExtension(new FileInfo(zipFile).FullName));
			return Directory.Exists(destinationPath) switch {
				true => new DirectoryInfo(destinationPath),
				false => Unzip(packageArchiver, new FileInfo(zipFile), destinationPath)
			};
		};
	
	/// <summary>
	/// This function is used to detect the type of a database from a directory.
	/// </summary>
	/// <param name="unzippedDirectory">The directory where the database files are expected to be found.</param>
	/// <returns>
	/// DatabaseType enum which indicates whether the database is of type MsSql or Postgres, 
	/// based on the respective file backup extensions.
	/// </returns>
	/// <exception cref="Exception">An exception is thrown if the backup file does not exist in the expected db folder.</exception>
	[NotNull]
	public static readonly Func<DirectoryInfo, DatabaseType> DetectDataBase = unzippedDirectory => {
		FileInfo fi = unzippedDirectory.GetDirectories("db")
			.FirstOrDefault()
			?.GetFiles().FirstOrDefault();

		return fi?.Extension switch {
			MsSqlBackupExtension => DatabaseType.MsSql,
			PgBackupExtension => DatabaseType.Postgres,
			_ => throw new Exception($"Backup file does not exist in db folder {unzippedDirectory}\\db")
		};
	};
	
	
	/// <summary>
	/// This function acts as a wrapper for performing file copying operations according to the given parameters.
	/// </summary>
	/// <param name="unzippedDirectory"> an instance of DirectoryInfo where the unzipped directory exists. </param>
	/// <param name="directFsAccess"> a Boolean flag determining the method of file copying, i.e., via direct file system or through stateful set name. </param>
	/// <param name="processExecutor"> an IProcessExecutor instance to be leveraged for process execution if necessary, only used when <paramref name="directFsAccess"/> is <c>false</c></param>
	/// <param name="destinationDirectory">a destination directory where backup file will be copied, only used when <paramref name="directFsAccess"/> is <c>true</c></param>
	/// <returns> Returns a tuple containing a Boolean value indicating the success of the operation and a string indicating the filename copied. </returns>
	/// <exception cref="Exception"> Throws an exception if the system is not able to find stateful set name for the given database type when directFsAccess is false. </exception>
	public static readonly Func<DirectoryInfo, bool, IProcessExecutor, string, DatabaseType, (bool success, string fileName)>
		CopyBackupFile = (unzippedDirectory, directFsAccess, processExecutor, destinationDirectory, dbType) => directFsAccess switch {
			true => FileSystemCpImpl(GetBackupFileInfoFromUnzippedFolder(unzippedDirectory),
				new DirectoryInfo(destinationDirectory)),
			false => FindStatefulSetName(dbType, processExecutor) switch {
				({ } podName, true) => KubectlCpImpl(podName, GetBackupFileInfoFromUnzippedFolder(unzippedDirectory),
					processExecutor, DetectDataBase(unzippedDirectory)),
				(not null, false) => throw new Exception(
					$"Cannot find stateful set name for database type {DetectDataBase(unzippedDirectory)}")
			}
		};
	
	/// <summary>
	/// Deletes a file from a specified location determined based on the DatabaseType.
	/// Makes use of Kubernetes command-line tool (kubectl) for deletion.
	/// </summary>
	/// <param name="filename">The name of file to be deleted.</param>
	/// <param name="processExecutor">The process executor to run commands.</param>
	/// <param name="dbType">The database type, used to determine the file location.</param>
	/// <returns>Returns true if the file deletion is a success, or false otherwise.</returns>
	public static readonly Func<string, IProcessExecutor, DatabaseType, bool> DeleteFileK8 =
		(filename, processExecutor, dbType) =>
			FindStatefulSetName(dbType, processExecutor) switch {
				(_, false) => false,
				(null, true) => throw new Exception("Filename cannot be empty"),
				({ } podName, true) => DeleteFileK8Impl(podName, filename, dbType, processExecutor)
			};

	/// <summary>
	/// Deletes a specific file in the file system.
	/// </summary>
	/// <param name="filename">The name of the file to delete.</param>
	/// <returns>
	/// Returns <c>true</c> if the file deletion was successful, <c>false</c> otherwise.
	/// </returns>
	/// <exception cref="Exception">Any exceptions thrown by the <see cref="System.IO.File.Delete(string)"/> method are suppressed and result in the return of <c>false</c>.</exception>
	[NotNull]
	public static readonly Func<string, bool> DeleteFileFs = filename => {
		try {
			File.Delete(filename);
			return true;
		} catch (Exception e) {
			return false;
		}
	};
	
	
	/// <summary>
	/// Detects the framework used in the provided directory.
	/// </summary>
	/// <param name="unzippedDirectory">The directory in which the framework type is to be detected.</param>
	/// <returns>
	/// Returns FrameworkType.NetFramework when a "Terrasoft.WebApp" directory exists in the provided directory.
	/// Returns FrameworkType.NetCore when  a "Terrasoft.Configuration" directory exists in the provided directory.
	/// </returns>
	/// <exception cref="Exception">Throws an exception if neither directory exists.</exception>
	[NotNull]
	public static readonly Func<DirectoryInfo, FrameworkType> DetectFramework = unzippedDirectory =>
		unzippedDirectory switch {
			not null when unzippedDirectory.GetDirectories("Terrasoft.WebApp").Any() => FrameworkType.NetFramework,
			not null when unzippedDirectory.GetDirectories("Terrasoft.Configuration").Any() => FrameworkType.NetCore,
			_ => throw new Exception(
				$"Cannot determine framework type, Terrasoft.WebApp or Terrasoft.WebAppCore does not exist in {unzippedDirectory}")
		};

	
	
	#endregion
	
	
	#region Fields: Private
	
	
	/// <summary>
	/// This function retrieves backup file from the unzipped directory. 
	/// The function is set as a Func delegate, taking as a parameter a DirectoryInfo object representing unzipped directory.
	/// It searches for directory named "db" within the unzipped directory. 
	/// If such directory is not found, the function throws an exception.
	/// If the "db" directory is found, it retrieves the first file with either a MsSqlBackupExtension or PgBackupExtension.
	/// </summary>
	/// <exception cref="System.Exception">Thrown when no 'db' folder is found in the specified directory</exception>
	/// <param name="unzippedDirectory">The DirectoryInfo object that represents the extracted directory.</param>
	/// <returns>Returns FileInfo object representing the backup file. Returns null in case no file with the required extensions is found.</returns>
	[NotNull]
	private static readonly Func<DirectoryInfo, FileInfo> GetBackupFileInfoFromUnzippedFolder = unzippedDirectory =>
		unzippedDirectory.GetDirectories("db") switch {
			{Length: 0} => throw new Exception($"No db folder found in {unzippedDirectory}"),
			{ } dbDir => dbDir.First().GetFiles()
				.FirstOrDefault(f => f.Extension is MsSqlBackupExtension or PgBackupExtension)
		};
	
	
	
	/// <summary>
	/// Creates a PostgreSQL template database in the Kubernetes environment, or returns false if the operation fails.
	/// </summary>
	/// <param name="podName">The name of the Kubernetes pod where PostgreSQL is running.</param>
	/// <param name="dbName">The name of the database to be created.</param>
	/// <param name="processExecutor">The process executor responsible for carrying out the command.</param>
	/// <returns>
	/// A boolean value indicating whether the database was successfully created.
	/// </returns>
	[NotNull]
	private static readonly Func<string, string ,IProcessExecutor, bool> CreatePostgresDbK8Impl = (podName, dbName, processExecutor)=> {
		string commandToExecute= $"psql -U postgres -c 'CREATE DATABASE \"{dbName}\" ENCODING=\"UTF8\" CONNECTION LIMIT=-1'";
		
		//This will only work on windows, or where PowerShell is installed.
		return processExecutor.Execute(PowershellCommandName,
			$"{KubectlCommandName} exec {podName} -n {K8NameSpace} -- {commandToExecute}", true,
			Environment.CurrentDirectory, true)=="CREATE DATABASE";
	};
	
	
	private static readonly Func<string, bool> CreateMsSqlDbWithClient = (dbName)=> {
		
		try
		{
			// Build connection string
			SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
			builder.DataSource = "localhost,1434";
			builder.UserID = "sa";
			builder.Password = "$Zarelon01$Zarelon01";
			builder.InitialCatalog = "master";
			builder.Encrypt = false;
			
			string cs = builder.ConnectionString;
			using SqlConnection connection = new SqlConnection(builder.ConnectionString);
			connection.Open();
			
			string sqlText = $@"

			USE [master]
			RESTORE DATABASE [8.1.1.1164_Studio_Softkey_MSSQL_ENU] 
			FROM  DISK = N'/var/opt/mssql/data/8.1.1.1164_Studio_Softkey_MSSQL_ENU.bak' 
			WITH  FILE = 1,  
			MOVE N'TSOnline_Data' 
			TO N'/var/opt/mssql/data/8.1.1.1164_Studio_Softkey_MSSQL_ENU.mdf',  
			MOVE N'TSOnline_Log' TO N'/var/opt/mssql/data/8.1.1.1164_Studio_Softkey_MSSQL_ENU_1.ldf',  
			NOUNLOAD,  STATS = 5

			";
			
			var cmd = new SqlCommand(sqlText, connection);
			var result = cmd.ExecuteNonQuery();
			
			connection.Close();
		}
		catch (SqlException e)
		{
			Console.WriteLine(e.ToString());
		}
		
		
		return 	true;
	};
	
	private static readonly Func<string, bool> CreatePostgresDbWithClient = (dbName)=> {
		
		try {
			// also valid
			// NpgsqlConnectionStringBuilder csb = new () {
			// 	Host = "localhost",
			// 	Port = 5432,
			// 	Username = "postgres",
			// 	Password = "root",
			// 	Database = "postgres"
			// };
			//using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(csb);
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PgConnectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	};
	
	
	
	
	private static readonly Func<string, string,string, IProcessExecutor, bool> RestorePostgresDbFromBackupK8Impl = (podName, dbName, fileName, processExecutor)=> {
		string commandToExecute= $"pg_restore /usr/local/backup-images/{fileName} --dbname={dbName} --no-owner --no-privileges --jobs=4 --username=postgres";
		//This will only work on windows, or where PowerShell is installed.
		
		// var result =  processExecutor.Execute(PowershellCommandName,
		// 	$"-Command \"{KubectlCommandName} exec -it {podName} -n {K8NameSpace} -- {commandToExecute}\"", true,
		// 	Environment.CurrentDirectory, true);
		var p = Process.Start("kubectl", $"exec -it {podName} -n {K8NameSpace} -- {commandToExecute}");
		p.WaitForExit();
		
		
		return true;
	};
	
	[NotNull]
	private static readonly Func<IPackageArchiver, FileInfo, string, DirectoryInfo> Unzip =
		(packageArchiver, sourceFile, destingationPath) => {
			packageArchiver.UnZip(sourceFile.FullName, false, Directory.CreateDirectory(destingationPath).FullName);
			return new DirectoryInfo(destingationPath);
		};
	
	

	/// <summary>
	/// This is a function that copies a given backup file to a specific file path 
	/// within a Pod (containerized application) in a Kubernetes cluster, 
	/// according to the type of database the backup is for.
	/// It renames the file to reflect the directory it was taken from.
	/// </summary>
	/// <param name="podName">The name of the Pod where the file needs to be copied to.</param>
	/// <param name="backupFile">The FileInfo object pertaining to the backup file.</param>
	/// <param name="processExecutor">The IProcessExecutor object to handle process executions.</param>
	/// <param name="dbType">The type of database the backup file is for.</param>
	/// <returns> Returns true if the file copy was successful, otherwise it returns false.</returns>
	private static readonly Func<string, FileInfo, IProcessExecutor, DatabaseType, ValueTuple<bool, string>>
		KubectlCpImpl = (podName, backupFile, processExecutor, dbType) => {
			string filename = backupFile.Name;

			string workingDirectory = backupFile.DirectoryName;
			string destinationDirectory = dbType switch {
				DatabaseType.MsSql => "/var/opt/mssql/data",
				DatabaseType.Postgres => "/usr/local/backup-images",
				_ => throw new Exception($"Unknown database type {dbType}")
			};

			string destFilename = ComposeDestinationFileName(backupFile);

			//kubectl cp BPMonline811SalesEnterprise_Marketing_ServiceEnterpriseNet6.backup clio-postgres-0:/usr/local/backup-images/BPMonline811SalesEnterprise_Marketing_ServiceEnterpriseNet6.backup
			string copyResult = processExecutor.Execute(KubectlCommandName,
				$"cp {filename} {podName}:{destinationDirectory}/{destFilename} -n {K8NameSpace}", true, workingDirectory, true);

			return (IsKubeCtlCommandSuccess(copyResult), destFilename);
		};

	/// <summary>
	/// Concatenates the parent directory name of a FileInfo object (representing a backup file) 
	/// with the extension of the backup file to construct a destination filename.
	/// </summary>
	/// <param name="backupFile">The file of which a backup created.</param>
	/// <returns>A string representing the destination filename.</returns>
	private static readonly Func<FileInfo, string> ComposeDestinationFileName = backupFile =>
		$"{backupFile.Directory.Parent.Name}{Path.GetExtension(backupFile.Name)}";

	/// <summary>
	/// Copies a file to a destination directory using the System.IO.File.Copy method.
	/// </summary>
	/// <remarks>
	/// This function attempts to copy a file to a directory. If there are any issues during the operation,
	/// it returns false along with the intended file name in the destination directory.
	/// </remarks>
	/// <param name="backupFile">The file to be copied.</param>
	/// <param name="destinationDirectory">The destination directory for the file.</param>
	/// <returns>A tuple containing a boolean indicating the success of the operation, and a string containing the name of the file in the destination directory.</returns>
	[NotNull]
	private static readonly Func<FileInfo, DirectoryInfo, ValueTuple<bool, string>> FileSystemCpImpl =
		(backupFile, destinationDirectory) => {
			string destFilename = ComposeDestinationFileName(backupFile);
			try {
				File.Copy(backupFile.FullName, Path.Join(destinationDirectory.FullName, destFilename));
				return (true, destFilename);
			} catch (Exception e) {
				return (false, destFilename);
			}
		};

	
	/// <summary>
	/// Delete a specified file from a Kubernetes pod in a specified DatabaseType directory,
	/// using a process executor to run the command. 
	/// </summary>
	/// <param name="podName">The name of the Kubernetes pod from which the file is to be deleted.</param>
	/// <param name="fileName">The name of the file to be deleted.</param>
	/// <param name="dbType">The type of database that decides the destination directory.</param>
	/// <param name="processExecutor">The executor that runs the kubectl command to delete the file from the pod.</param>
	/// <returns>
	/// Returns a boolean indicating whether the file deletion was successful. Success or failure is determined by the IsKubeCtlCommandSuccess method.
	/// </returns>
	[NotNull]
	private static readonly Func<string, string, DatabaseType, IProcessExecutor, bool> DeleteFileK8Impl =
		(podName, fileName, dbType, processExecutor) => {
			string destinationDirectory = dbType switch {
				DatabaseType.MsSql => "/var/opt/mssql/data",
				DatabaseType.Postgres => "/usr/local/backup-images",
				_ => throw new Exception($"Unknown database type {dbType}")
			};
			string copyResult = processExecutor.Execute(KubectlCommandName,
				$"exec {podName} -n {K8NameSpace} -- rm {destinationDirectory}/{fileName}", true,
				Environment.CurrentDirectory, true);
			return IsKubeCtlCommandSuccess(copyResult);
		};

	

	/// <summary>
	/// The delegate is responsible for determining the stateful set name for a given database type.
	/// </summary>
	/// <param name="DatabaseType">The type of database for which to find the stateful set name.</param>
	/// <param name="IProcessExecutor">An interface that provides mechanisms for executing a process.</param>
	/// <returns>The name of the stateful set for the given database type or error string</returns>
	/// <remarks>
	/// The name of the stateful set for the given database type.
	/// The stateful set name is determined as follows:
	/// 1. Depending on the given DatabaseType, the appropriate app name is selected.
	/// 2. The command arguments for the 'kubectl get pods' command are constructed, which are used to fetch the pod information for the selected app.
	/// 3. The 'kubectl get pods' command is executed using the provided process executor.
	/// 4. The command returns the name of the first pod for the app, which is assumed to be the stateful set name.
	/// Note that 'kubectl -n clio-infrastructure get pods -l app=clio-postgres -o=jsonpath='{.items[0].metadata.name}''
	/// is an example of how the executed command might look like.
	/// </remarks>
	[NotNull]
	private static readonly Func<DatabaseType, IProcessExecutor, ValueTuple<string, bool>> FindStatefulSetName =
		(dbType, processExecutor) => {
			string appName = dbType switch {
				DatabaseType
					.MsSql => MssqlAppName, //I should fix this in deployment, to make sure all names start with clio-
				DatabaseType.Postgres => PostgresAppName,
				_ => throw new Exception($"Unknown database type {dbType}")
			};
			string commandArgs =
				$"-n {K8NameSpace} get pods -l app={appName} -o=jsonpath='{{.items[0].metadata.name}}'";

			//kubectl -n clio-infrastructure get pods -l app=clio-postgres -o=jsonpath='{.items[0].metadata.name}'
			string result =
				processExecutor.Execute(KubectlCommandName, commandArgs, true, Environment.CurrentDirectory);

			return IsKubeCtlCommandSuccess(result) switch {
				true => new ValueTuple<string, bool>(result.Replace("'", string.Empty), true),
				false => new ValueTuple<string, bool>(result, false)
			};
		};

	/// <summary>
	/// Checks if the resulting string is error
	/// </summary>
	/// <param name="value">The output of the command executed in Kubernetes</param>
	/// <returns><c>False</c> if the output begins with the string "error" (case-insensitive), otherwise true</returns>
	[NotNull]
	private static readonly Func<string, bool> IsKubeCtlCommandSuccess = value => !value
		.ToLower(CultureInfo.InvariantCulture).StartsWith("error");

	#endregion

}