using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;

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
		try
		{
			FQDN =  Dns.GetHostName();
			FQDN = Dns.GetHostEntry(Dns.GetHostName()).HostName;
			return FQDN;
		}
		catch (Exception e)
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
	public static readonly Func<string, IPackageArchiver, DirectoryInfo> UnzipOrTakeExisting =
		(zipFile, packageArchiver) => {
			string destinationPath = Path.Join(new FileInfo(zipFile).Directory.FullName, Path.GetFileNameWithoutExtension(new FileInfo(zipFile).FullName));
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
			_ => throw new Exception(
				$"Cannot determine framework type, Terrasoft.WebApp or Terrasoft.WebAppCore does not exist in {unzippedDirectory}")
		};
	#endregion
	
	
	#region Fields: Private
	
	
	[NotNull]
	private static readonly Func<IPackageArchiver, FileInfo, string, DirectoryInfo> Unzip =
		(packageArchiver, sourceFile, destingationPath) => {
			packageArchiver.UnZip(sourceFile.FullName, false, Directory.CreateDirectory(destingationPath).FullName);
			return new DirectoryInfo(destingationPath);
		};

	#endregion

}