using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Clio.Common.IIS;

public class WindowsIISAppPoolManager : IIISAppPoolManager
{
	private const string AppCmdPath = @"C:\Windows\System32\inetsrv\appcmd.exe";

	private string ExecuteAppCmd(string arguments)
	{
		try
		{
			if (!File.Exists(AppCmdPath))
			{
				return string.Empty;
			}

			using Process process = new()
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = AppCmdPath,
					Arguments = arguments,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};

			process.Start();
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

			return output;
		}
		catch
		{
			return string.Empty;
		}
	}

	public async Task<string> GetAppPoolState(string appPoolName)
	{
		return await Task.Run(() =>
		{
			if (string.IsNullOrWhiteSpace(appPoolName))
			{
				return "Unknown";
			}

			// Check if IIS/appcmd is available first
			if (!File.Exists(AppCmdPath))
			{
				return "Unknown";
			}

			string appPoolXml = ExecuteAppCmd($"list apppool \"{appPoolName}\" /xml");
			if (string.IsNullOrWhiteSpace(appPoolXml))
			{
				return "NotFound";
			}

			try
			{
				XElement appPoolRoot = XElement.Parse(appPoolXml);
				XElement appPoolElement = appPoolRoot.Element("APPPOOL");
				if (appPoolElement != null)
				{
					return appPoolElement.Attribute("state")?.Value ?? "Unknown";
				}
			}
			catch
			{
				return "Unknown";
			}

			return "NotFound";
		});
	}

	public async Task<bool> IsAppPoolRunning(string appPoolName)
	{
		string state = await GetAppPoolState(appPoolName);
		return state == "Started";
	}

	public async Task<bool> StartAppPool(string appPoolName)
	{
		return await Task.Run(() =>
		{
			if (string.IsNullOrWhiteSpace(appPoolName))
			{
				return false;
			}

			string currentState = GetAppPoolState(appPoolName).GetAwaiter().GetResult();
			if (currentState == "NotFound")
			{
				return false;
			}

			if (currentState == "Started")
			{
				return true;
			}

			string result = ExecuteAppCmd($"start apppool \"{appPoolName}\"");
			return !string.IsNullOrWhiteSpace(result) && !result.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
		});
	}

	public async Task<bool> StopAppPool(string appPoolName)
	{
		return await Task.Run(() =>
		{
			if (string.IsNullOrWhiteSpace(appPoolName))
			{
				return false;
			}

			string currentState = GetAppPoolState(appPoolName).GetAwaiter().GetResult();
			if (currentState == "NotFound")
			{
				return false;
			}

			if (currentState == "Stopped")
			{
				return true;
			}

			string result = ExecuteAppCmd($"stop apppool \"{appPoolName}\"");
			return !string.IsNullOrWhiteSpace(result) && !result.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
		});
	}

	public async Task<bool> StartSite(string siteName)
	{
		return await Task.Run(() =>
		{
			if (string.IsNullOrWhiteSpace(siteName))
			{
				return false;
			}

			string result = ExecuteAppCmd($"start site \"{siteName}\"");
			return !string.IsNullOrWhiteSpace(result) && !result.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
		});
	}

	public async Task<bool> IsSiteRunning(string siteName)
	{
		return await Task.Run(() =>
		{
			if (string.IsNullOrWhiteSpace(siteName))
			{
				return false;
			}

			string siteXml = ExecuteAppCmd($"list site \"{siteName}\" /xml");
			if (string.IsNullOrWhiteSpace(siteXml))
			{
				return false;
			}

			try
			{
				XElement siteRoot = XElement.Parse(siteXml);
				XElement siteElement = siteRoot.Element("SITE");
				if (siteElement != null)
				{
					string state = siteElement.Attribute("state")?.Value ?? "Unknown";
					return state == "Started";
				}
			}
			catch
			{
				return false;
			}

			return false;
		});
	}
}
