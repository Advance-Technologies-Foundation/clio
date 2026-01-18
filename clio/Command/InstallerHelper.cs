using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using Clio.Common;
using Terrasoft.Web.Common;

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

	private const string MsSqlBackupExtension = ".bak";
	private const string PgBackupExtension = ".backup";
	
	#endregion

	#region Fields: Public
	
	
	public static string FetFQDN() {
		string FQDN = "localhost";
		try {
			FQDN =  Dns.GetHostName();
			FQDN = Dns.GetHostEntry(Dns.GetHostName()).HostName;
			return FQDN;
		}
		catch (Exception)
		{
			Console.WriteLine("Defaulting to localhost");
			return FQDN;
		}
	}
	
	
	/// <summary>
	/// Represents a function that takes a zip file and a package archiver as parameters,
	/// checks if a directory with the same name as the zip file (without the extension) exists.
	/// If it does, it returns the directory info. If not, it unzips the file and returns the info of the new directory.
	/// </summary>
	/// <param name="zipFile">The full path of the zip file.</param>
	/// <param name="packageArchiver">The package archiver instance to be used for the unzipping process.</param>
	/// <returns>The DirectoryInfo object representing the existing or newly created directory.</returns>
	[NotNull]
	public static readonly Func<string, IPackageArchiver, DirectoryInfo> UnzipOrTakeExistingOld =
		(zipFile, packageArchiver) => {


			//Already unzipped
			if (Directory.Exists(zipFile)) {
				return TakeExisting(zipFile);
			}
			
			
			string destinationPath = Path.Join(new FileInfo(zipFile).Directory.FullName, Path.GetFileNameWithoutExtension(new FileInfo(zipFile).FullName));
			return Directory.Exists(destinationPath) switch {
					   true => TakeExisting(destinationPath),
					   false => Unzip(packageArchiver, new FileInfo(zipFile), destinationPath)
				   };
		};
	
	
	/// <summary>
	/// Represents a function that takes a zip file, target folder, and a package archiver as parameters.
	/// Checks if the target directory exists and contains essential files (db folder).
	/// If it does, it returns the directory info. If not, it unzips the file directly into target folder and returns the info.
	/// </summary>
	/// <param name="zipFile">The full path of the zip file.</param>
	/// <param name="targetFolder">The target folder where ZIP should be extracted (e.g., ~/creatio_app/mac_env).</param>
	/// <param name="packageArchiver">The package archiver instance to be used for the unzipping process.</param>
	/// <returns>The DirectoryInfo object representing the existing or newly created directory.</returns>
	[NotNull]
	public static readonly Func<string, string, IPackageArchiver, DirectoryInfo> UnzipOrTakeExisting = 
		(zipFile, targetFolder, packageArchiver) => {
			ILogger logger = ConsoleLogger.Instance;
			logger.WriteDebug("UnzipOrTakeExisting - Starting");
			logger.WriteDebug($"UnzipOrTakeExisting - Zip file: {zipFile}");
			logger.WriteDebug($"UnzipOrTakeExisting - Target folder: {targetFolder}");
			logger.WriteDebug($"UnzipOrTakeExisting - Directory exists: {Directory.Exists(targetFolder)}");
			
			// Check if directory exists and contains extracted application files
			// We check for key application files/folders that indicate successful extraction
			// Note: 'db' folder is created by database restore script, not included in archive
			if (Directory.Exists(targetFolder)) {
				var existingDir = new DirectoryInfo(targetFolder);
				var files = existingDir.GetFiles();
				var directories = existingDir.GetDirectories();
				
				// Check if extraction appears complete: has files and subdirectories
				// (typically includes bin, App_Data, ConnectionStrings.config, etc.)
				if (files.Length > 0 && directories.Length > 0) {
					logger.WriteDebug("UnzipOrTakeExisting - Directory exists and appears complete (has files and folders)");
					return existingDir;
				} else {
					logger.WriteDebug(" UnzipOrTakeExisting - Directory exists but appears incomplete (missing files), re-extracting");
					// Remove incomplete directory and extract fresh
					try {
						Directory.Delete(targetFolder, true);
						logger.WriteDebug(" UnzipOrTakeExisting - Removed incomplete directory");
					} catch (Exception ex) {
						logger.WriteError($"UnzipOrTakeExisting - Failed to remove incomplete directory: {ex.Message}");
					}
				}
			}
			
			return Unzip(packageArchiver, new FileInfo(zipFile), targetFolder);
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
		ILogger logger = ConsoleLogger.Instance;
		logger.WriteDebug($"DetectDataBase - Searching for backup in: {unzippedDirectory.FullName}");
		var dbDirectory = unzippedDirectory.GetDirectories("db").FirstOrDefault();
		if (dbDirectory == null) {
			logger.WriteDebug("DetectDataBase - db directory not found!");
			throw new Exception($"db folder does not exist in {unzippedDirectory.FullName}");
		}
		logger.WriteDebug($"DetectDataBase - db directory found: {dbDirectory.FullName}");
		FileInfo[] files = dbDirectory.GetFiles();
		logger.WriteDebug($"DetectDataBase - Files in db folder: {string.Join(", ", files.Select(f => f.Name))}");
		FileInfo fi = files.FirstOrDefault();

		if (fi == null) {
			logger.WriteDebug("DetectDataBase - No backup file found in db folder!");
			throw new Exception($"Backup file does not exist in db folder {dbDirectory.FullName}");
		}

		logger.WriteDebug($"DetectDataBase - Found backup file: {fi.Name}, Extension: {fi.Extension}");
		return fi.Extension switch {
			MsSqlBackupExtension => DatabaseType.MsSql,
			PgBackupExtension => DatabaseType.Postgres,
			_ => throw new Exception($"Unknown backup file extension: {fi.Extension} for file {fi.FullName}")
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
	public static readonly Func<DirectoryInfo, FrameworkType> DetectFramework = unzippedDirectory => unzippedDirectory switch {
			not null when unzippedDirectory.GetDirectories("Terrasoft.WebApp").Any() => FrameworkType.NetFramework,
			not null when unzippedDirectory.GetDirectories("Terrasoft.Configuration").Any() => FrameworkType.NetCore,
			var _ => throw new Exception(
				$"Cannot determine framework type, Terrasoft.WebApp or Terrasoft.WebAppCore does not exist in {unzippedDirectory}")
		};
	#endregion
	
	
	#region Fields: Private

	[NotNull]
	private static readonly Func<string, DirectoryInfo> TakeExisting =
		(destinationPath) => new DirectoryInfo(destinationPath);
	
	[NotNull]
	private static readonly Func<IPackageArchiver, FileInfo, string, DirectoryInfo> Unzip =
		(packageArchiver, sourceFile, destinationPath) => {
			ILogger logger = ConsoleLogger.Instance;
			logger.WriteDebug($"Unzip - Creating directory: {destinationPath}");
			Directory.CreateDirectory(destinationPath);
			logger.WriteDebug("Unzip - Directory created successfully");
			logger.WriteInfo($"Unzip - Extracting zip: {sourceFile.FullName} to {destinationPath}");
			packageArchiver.UnZip(sourceFile.FullName, false, destinationPath);
			logger.WriteDebug("Unzip - Extraction completed successfully");
			DirectoryInfo resultDir = new (destinationPath);
			logger.WriteDebug($"Unzip - Result directory: {resultDir.FullName}, Exists: {resultDir.Exists}, Files count: {resultDir.GetFiles().Length}");
			logger.WriteInfo($"[Unzip completed] - {destinationPath}");
			return resultDir;
		};

	#endregion

}
