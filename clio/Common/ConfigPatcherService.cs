using System;
using System.IO;
using System.IO.Abstractions;
using System.Xml.Linq;

namespace Clio.Common
{
	public interface IConfigPatcherService
	{
		bool PatchCookiesSameSiteMode(string configFile);
		bool UpdateConnectionString(string configFile, string server, int port, string database, string username, string password);
		bool ConfigurePort(string configFile, int port);
	}

	public class ConfigPatcherService : IConfigPatcherService
	{
		private readonly System.IO.Abstractions.IFileSystem _fileSystem;

		public ConfigPatcherService(System.IO.Abstractions.IFileSystem fileSystem = null)
		{
			_fileSystem = fileSystem;
		}

		public bool PatchCookiesSameSiteMode(string configFile)
		{
			try
			{
				if (_fileSystem != null)
				{
					if (!_fileSystem.File.Exists(configFile))
						return false;

					var content = _fileSystem.File.ReadAllText(configFile);
					var doc = XDocument.Parse(content);
					
					if (doc.Root == null)
						return false;

					var systemWebServer = doc.Root.Element("system.webServer");
					if (systemWebServer == null)
						return false;

					var httpCookies = systemWebServer.Element("httpCookies");
					if (httpCookies == null)
					{
						httpCookies = new XElement("httpCookies");
						systemWebServer.Add(httpCookies);
					}

					httpCookies.SetAttributeValue("sameSite", "Lax");
					_fileSystem.File.WriteAllText(configFile, doc.ToString());
					return true;
				}
				else
				{
					if (!File.Exists(configFile))
						return false;

					var content = File.ReadAllText(configFile);
					var doc = XDocument.Parse(content);
					
					if (doc.Root == null)
						return false;

					var systemWebServer = doc.Root.Element("system.webServer");
					if (systemWebServer == null)
						return false;

					var httpCookies = systemWebServer.Element("httpCookies");
					if (httpCookies == null)
					{
						httpCookies = new XElement("httpCookies");
						systemWebServer.Add(httpCookies);
					}

					httpCookies.SetAttributeValue("sameSite", "Lax");
					File.WriteAllText(configFile, doc.ToString());
					return true;
				}
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool UpdateConnectionString(string configFile, string server, int port, string database, string username, string password)
		{
			try
			{
				if (_fileSystem != null)
				{
					if (!_fileSystem.File.Exists(configFile))
						return false;

					var content = _fileSystem.File.ReadAllText(configFile);
					var doc = XDocument.Parse(content);

					if (doc.Root == null)
						return false;

					var connectionStrings = doc.Root.Element("connectionStrings");
					if (connectionStrings == null)
					{
						connectionStrings = new XElement("connectionStrings");
						doc.Root.Add(connectionStrings);
					}

					var connectionString = $"Server={server},{port};Database={database};User Id={username};Password={password};";
					var addElement = connectionStrings.Element("add");
					if (addElement == null)
					{
						addElement = new XElement("add");
						connectionStrings.Add(addElement);
					}

					addElement.SetAttributeValue("name", "default");
					addElement.SetAttributeValue("connectionString", connectionString);
					_fileSystem.File.WriteAllText(configFile, doc.ToString());
					return true;
				}
				else
				{
					if (!File.Exists(configFile))
						return false;

					var content = File.ReadAllText(configFile);
					var doc = XDocument.Parse(content);

					if (doc.Root == null)
						return false;

					var connectionStrings = doc.Root.Element("connectionStrings");
					if (connectionStrings == null)
					{
						connectionStrings = new XElement("connectionStrings");
						doc.Root.Add(connectionStrings);
					}

					var connectionString = $"Server={server},{port};Database={database};User Id={username};Password={password};";
					var addElement = connectionStrings.Element("add");
					if (addElement == null)
					{
						addElement = new XElement("add");
						connectionStrings.Add(addElement);
					}

					addElement.SetAttributeValue("name", "default");
					addElement.SetAttributeValue("connectionString", connectionString);
					File.WriteAllText(configFile, doc.ToString());
					return true;
				}
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool ConfigurePort(string configFile, int port)
		{
			try
			{
				if (_fileSystem != null)
				{
					if (!_fileSystem.File.Exists(configFile))
						return false;

					var content = _fileSystem.File.ReadAllText(configFile);
					var doc = XDocument.Parse(content);
					
					if (doc.Root == null)
						return false;

					var appSettings = doc.Root.Element("appSettings");
					if (appSettings == null)
					{
						appSettings = new XElement("appSettings");
						doc.Root.Add(appSettings);
					}

					var portElement = appSettings.Element("add");
					if (portElement == null)
					{
						portElement = new XElement("add");
						appSettings.Add(portElement);
					}

					portElement.SetAttributeValue("key", "Port");
					portElement.SetAttributeValue("value", port.ToString());
					_fileSystem.File.WriteAllText(configFile, doc.ToString());
					return true;
				}
				else
				{
					if (!File.Exists(configFile))
						return false;

					var content = File.ReadAllText(configFile);
					var doc = XDocument.Parse(content);
					
					if (doc.Root == null)
						return false;

					var appSettings = doc.Root.Element("appSettings");
					if (appSettings == null)
					{
						appSettings = new XElement("appSettings");
						doc.Root.Add(appSettings);
					}

					var portElement = appSettings.Element("add");
					if (portElement == null)
					{
						portElement = new XElement("add");
						appSettings.Add(portElement);
					}

					portElement.SetAttributeValue("key", "Port");
					portElement.SetAttributeValue("value", port.ToString());
					File.WriteAllText(configFile, doc.ToString());
					return true;
				}
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
