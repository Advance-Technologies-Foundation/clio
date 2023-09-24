using System;
using System.Diagnostics;
using System.IO;
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
	
	
	public InstallerCommand(IPackageArchiver packageArchiver, IProcessExecutor processExecutor) {
		_packageArchiver = packageArchiver;
		_processExecutor = processExecutor;
	}

	private int DoPgWork(DirectoryInfo unzippedDirectory, string zipFile) {
		
		string tmpName = "template_"+unzippedDirectory.Name;
		bool exists = InstallerHelper.CheckPgTemplateExists(tmpName);
		if(exists) {
			return 0;
		}
		
		(bool isSuccess, string fileName) k8result = InstallerHelper.CopyBackupFile(unzippedDirectory, false, _processExecutor, null, InstallerHelper.DatabaseType.Postgres );
		Console.WriteLine($"[Copying backup file completed] - {k8result.fileName}");
		Console.WriteLine();
		
		string newDbName = "template_"+Path.GetFileNameWithoutExtension(new FileInfo(zipFile).Name);
		var createTemplateresult = InstallerHelper.CreatePostgresDb(newDbName, _processExecutor);
		Console.WriteLine($"[Created new database] - {newDbName}");
		Console.WriteLine();
		
		Console.WriteLine($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");
		var restoreResult = InstallerHelper.RestorePostgresDbFromBackup(newDbName, k8result.fileName, _processExecutor);
		InstallerHelper.SetPgDatabaseAsTemplate(newDbName);
		Console.WriteLine($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
		Console.WriteLine();
		
		InstallerHelper.DeleteFileK8(k8result.fileName, _processExecutor, InstallerHelper.DatabaseType.Postgres);
		Console.WriteLine($"[Deleted backup file] {k8result.fileName}");
		
		InstallerHelper.CreateDbFromTemplate(newDbName, newDbName.Replace("template_", string.Empty));
		Console.WriteLine($"[Database created] - {newDbName.Replace("template_", string.Empty)}");
		
		return 0;
	}
	
	private int DoMsWork(DirectoryInfo unzippedDirectory, string zipFile) {
		// (bool isSuccess, string fileName) k8result = InstallerHelper.CopyBackupFile(unzippedDirectory, false, _processExecutor, null, InstallerHelper.DatabaseType.MsSql);
		// Console.WriteLine($"[Copying backup file completed] - {k8result.fileName}");
		// Console.WriteLine();
		
		string newDbName = Path.GetFileNameWithoutExtension(new FileInfo(zipFile).Name);
		//var createTemplateresult = InstallerHelper.CreateMsSqlDb(newDbName, _processExecutor);
		
		//InstallerHelper.DeleteFileK8(k8result.fileName, _processExecutor, InstallerHelper.DatabaseType.Postgres);
		InstallerHelper.DeleteFileK8("8.1.1.1164_Studio_Softkey_MSSQL_ENU.bak", _processExecutor, InstallerHelper.DatabaseType.MsSql);
		//Console.WriteLine($"[Deleted backup file] {k8result.fileName}");
		
		return 0;
	}
	
	
	public override int Execute(PfInstallerOptions options) {
		
		Console.WriteLine($"[Staring unzipping] - {options.ZipFile}");
		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExisting(options. ZipFile, _packageArchiver);
		Console.WriteLine($"[Unzip completed] - {unzippedDirectory.FullName}");
		Console.WriteLine();
		
		var dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		var fwType = InstallerHelper.DetectFramework(unzippedDirectory);
		
		_ = InstallerHelper.DetectDataBase(unzippedDirectory) switch {
			InstallerHelper.DatabaseType.Postgres => DoPgWork(unzippedDirectory, options.ZipFile),
			InstallerHelper.DatabaseType.MsSql => DoMsWork(unzippedDirectory, options.ZipFile),
		};
		
	
		return 1;
	}
	
}