using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using Clio.Common;
using CommandLine;

namespace Clio.Command;


[Verb("pg-install", HelpText = "pg-install")]
public class PfInstallerOptions : EnvironmentNameOptions
{

	[Option("ZipFile", Required = true, HelpText = "Sets Zip File path")]
	public string ZipFile {
		get; set;
	}
}

public class InstallerCommand : Command<PfInstallerOptions>
{

	private readonly IPackageArchiver _packageArchiver;
	private readonly IProcessExecutor _processExecutor;
	private const string RootDestinationDirectory = @"D:\Projects\CreatioProductBuild"; 
	
	public InstallerCommand(IPackageArchiver packageArchiver, IProcessExecutor processExecutor) {
		_packageArchiver = packageArchiver;
		_processExecutor = processExecutor;
	}

	public override int Execute(PfInstallerOptions options) {

		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExisting(options. ZipFile, _packageArchiver);
		var dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		var fwType = InstallerHelper.DetectFramework(unzippedDirectory);
		
		InstallerHelper.KubectlCp(unzippedDirectory, _processExecutor, dbType);
		InstallerHelper.DeleteFile("BPMonline811SalesEnterprise_Marketing_ServiceEnterpriseNet6.backup", _processExecutor, dbType);
		
		
		return 1;
	}
	
}

public static class InstallerHelper
{
	private const string RootDestinationDirectory = @"D:\Projects\CreatioProductBuild";
	private const string PgBackupExtension = ".backup";
	private const string MsSqlBackupExtension = ".bak";
	private const string mssqlAppName = "mssql-clio";
	private const string postgresAppName = "clio-postgres";
	private const string kubectlCommandName = "kubectl";
	private const string k8NameSpace = "clio-infrastructure";
	
	public enum DatabaseType
	{

		Postgres, MsSql	

	}

	public enum FrameworkType
	{

		NetFramework, NetCore

	}
	
	[NotNull]
	public static readonly Func<string, IPackageArchiver, DirectoryInfo> UnzipOrTakeExisting =(zipFile, packageArchiver)=> {
		string destinationPath = Path.Join(RootDestinationDirectory, Path.GetFileNameWithoutExtension(new FileInfo(zipFile).FullName));
		return Directory.Exists(destinationPath) switch {
			true => new DirectoryInfo(destinationPath),
			false => Unzip(packageArchiver, new FileInfo(zipFile), destinationPath)
		};
	};
	
	[NotNull]
	private static readonly Func<IPackageArchiver, FileInfo, string, DirectoryInfo> Unzip=(packageArchiver,sourceFile, destingationPath)=> {
		packageArchiver.UnZip(sourceFile.FullName, false, Directory.CreateDirectory(destingationPath).FullName);
		return new DirectoryInfo(destingationPath);
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
	public static readonly Func<DirectoryInfo, DatabaseType> DetectDataBase = (unzippedDirectory)=> {
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
	/// Detects the framework used in the provided directory.
	/// </summary>
	/// <param name="unzippedDirectory">The directory in which the framework type is to be detected.</param>
	/// <returns>
	/// Returns FrameworkType.NetFramework when a "Terrasoft.WebApp" directory exists in the provided directory.
	/// Returns FrameworkType.NetCore when  a "Terrasoft.Configuration" directory exists in the provided directory.
	/// </returns>
	/// <exception cref="Exception">Throws an exception if neither directory exists.</exception>
	[NotNull]
	public static readonly Func<DirectoryInfo, FrameworkType> DetectFramework = (unzippedDirectory)=> unzippedDirectory switch {
		not null when unzippedDirectory.GetDirectories("Terrasoft.WebApp").Any() => FrameworkType.NetFramework,
		not null when unzippedDirectory.GetDirectories("Terrasoft.Configuration").Any() => FrameworkType.NetCore,
		_ => throw new Exception($"Cannot determine framework type, Terrasoft.WebApp or Terrasoft.WebAppCore does not exist in {unzippedDirectory}")
	};
	
	//This should return file name it creaed
	public static readonly Action<DirectoryInfo, IProcessExecutor, DatabaseType> KubectlCp = (unzippedDirectory, processExecutor, dbType)=> {
		
		var file = unzippedDirectory.GetDirectories("db").FirstOrDefault().GetFiles().FirstOrDefault();
		_ = FindStateFulSetName(DatabaseType.Postgres, processExecutor) switch {
			({ } podName, true) => CopyFile(podName, file,processExecutor, dbType),
			(not null, false) => throw new Exception($"Cannot find stateful set name for database type {dbType}")
		};
	};
	
	/// <summary>
	/// This is a function that copies a given backup file to a specific file path 
	/// within a Pod (containerized application) in a Kubernetes cluster, 
	/// according to the type of database the backup is for.
	/// </summary>
	/// <param name="podName">The name of the Pod where the file needs to be copied to.</param>
	/// <param name="backupFile">The FileInfo object pertaining to the backup file.</param>
	/// <param name="processExecutor">The IProcessExecutor object to handle process executions.</param>
	/// <param name="dbType">The type of database the backup file is for.</param>
	/// <returns> Returns true if the file copy was successful, otherwise it returns false.</returns>
	private static readonly Func<string, FileInfo, IProcessExecutor, DatabaseType, bool> CopyFile = (podName, backupFile, processExecutor,dbType)=> {
		string filename = backupFile.Name;
		string workingDirectory = backupFile.DirectoryName;
		string destinationDirectory = dbType switch {
			DatabaseType.MsSql => "/var/opt/mssql/data",
			DatabaseType.Postgres => "/usr/local/backup-images",
			_ => throw new Exception($"Unknown database type {dbType}")
		};
		
		//kubectl cp BPMonline811SalesEnterprise_Marketing_ServiceEnterpriseNet6.backup clio-postgres-0:/usr/local/backup-images/BPMonline811SalesEnterprise_Marketing_ServiceEnterpriseNet6.backup
		string copyResult = processExecutor.Execute(kubectlCommandName, $"cp {filename} {podName}:{destinationDirectory}/{filename}", true, workingDirectory, true);
		return IsKubeCtlCommandSuccess(copyResult);
	};
	
	
	/// <summary>
	/// Deletes a file from a specified location determined based on the DatabaseType.
	/// Makes use of Kubernetes command-line tool (kubectl) for deletion.
	/// </summary>
	/// <param name="filename">The name of file to be deleted.</param>
	/// <param name="processExecutor">The process executor to run commands.</param>
	/// <param name="dbType">The database type, used to determine the file location.</param>
	/// <returns>Returns true if the file deletion is a success, or false otherwise.</returns>
	public static readonly Func<string, IProcessExecutor, DatabaseType, bool> DeleteFile = (filename, processExecutor, dbType)=> {
		(string podName, bool isSuccess) = FindStateFulSetName(DatabaseType.Postgres, processExecutor);
		if(!isSuccess) {
			return false;
		} 
		
		string destinationDirectory = dbType switch {
			DatabaseType.MsSql => "/var/opt/mssql/data",
			DatabaseType.Postgres => "/usr/local/backup-images",
			_ => throw new Exception($"Unknown database type {dbType}")
		};
		
		// kubectl exec <pod-name> -n <namespace> -- rm /path/to/your/file
		string copyResult = processExecutor.Execute(kubectlCommandName, $"exec {podName} -n {k8NameSpace} -- rm {destinationDirectory}/{filename}", true, Environment.CurrentDirectory, true);
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
	private static readonly Func<DatabaseType, IProcessExecutor, ValueTuple<string, bool>> FindStateFulSetName = (dbType,processExecutor)=> {
		string appName = dbType switch {
			DatabaseType.MsSql => mssqlAppName, //I should fix this in deployment, to make sure all names start with clio-
			DatabaseType.Postgres => postgresAppName,
			_ => throw new Exception($"Unknown database type {dbType}")
		};
		string commandArgs = $"-n {k8NameSpace} get pods -l app={appName} -o=jsonpath='{{.items[0].metadata.name}}'"; 
		
		//kubectl -n clio-infrastructure get pods -l app=clio-postgres -o=jsonpath='{.items[0].metadata.name}'
		string result =  processExecutor.Execute(kubectlCommandName,commandArgs, true, Environment.CurrentDirectory);
		
		return IsKubeCtlCommandSuccess(result) switch {
			true => new ValueTuple<string, bool>(result.Replace("'",string.Empty), true),
			false => new ValueTuple<string, bool>(result, false)
		};
	};
	
	[NotNull]
	private static readonly Func<string, bool> IsKubeCtlCommandSuccess = (value) => !value
		.ToLower(CultureInfo.InvariantCulture).StartsWith("error");
}