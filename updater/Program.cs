using System;
using System.IO;
using System.Threading;

namespace updater
{
	class Program
	{
		static void Main(string[] args) {
			Thread.Sleep(500);
			var dir = AppDomain.CurrentDomain.BaseDirectory;
			string tempDirPath = Path.Combine(dir, "Update", "Temp");
			foreach (var filePath in Directory.GetFiles(tempDirPath)) {
				var fileInfo = new FileInfo(filePath);
				if (fileInfo.Name != "updater.dll") {
					fileInfo.CopyTo(Path.Combine(dir, fileInfo.Name), true);
				}
			}
			Console.WriteLine("Update completed. Press enter to continue.");
		}
	}
}
