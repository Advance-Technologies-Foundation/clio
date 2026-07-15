using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Xml;

namespace Clio.Common.IIS;

/// <summary>
/// Configures the HTTPS-specific settings required by a .NET Framework Creatio deployment.
/// </summary>
public interface INetFrameworkHttpsConfigurator {
	/// <summary>Applies idempotent HTTPS configuration beneath <paramref name="applicationPath"/>.</summary>
	void Configure(string applicationPath);
}

/// <summary>
/// Applies XML-aware, whitespace-preserving .NET Framework HTTPS configuration changes.
/// </summary>
public sealed class NetFrameworkHttpsConfigurator(System.IO.Abstractions.IFileSystem fileSystem) : INetFrameworkHttpsConfigurator {
	private const string HttpBehaviorPath = @"Terrasoft.WebApp\ServiceModel\http\behaviors.config";
	private const string HttpsBehaviorPath = @"Terrasoft.WebApp\ServiceModel\https\behaviors.config";
	private const string HttpBindingPath = @"Terrasoft.WebApp\ServiceModel\http\bindings.config";
	private const string HttpsBindingPath = @"Terrasoft.WebApp\ServiceModel\https\bindings.config";
	private const string ConfigSourceAttribute = "configSource";
	private const string WsServiceType = "Terrasoft.Messaging.MicrosoftWSService.MicrosoftWSService";
	private readonly System.IO.Abstractions.IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	/// <inheritdoc />
	public void Configure(string applicationPath) {
		string rootPath = _fileSystem.Path.Combine(applicationPath, "Web.config");
		string innerPath = _fileSystem.Path.Combine(applicationPath, "Terrasoft.WebApp", "Web.config");
		XmlDocument root = Load(rootPath);
		XmlDocument inner = Load(innerPath);

		bool behaviorChanged = SetConfigSource(root, "behaviors", HttpBehaviorPath, HttpsBehaviorPath);
		bool bindingChanged = SetConfigSource(root, "bindings", HttpBindingPath, HttpsBindingPath);
		bool rootChanged = behaviorChanged || bindingChanged;
		XmlElement[] wsServices = inner.SelectNodes("//wsService[@type]")!
			.Cast<XmlElement>()
			.Where(element => element.GetAttribute("type").Contains(WsServiceType, StringComparison.Ordinal))
			.ToArray();
		if (wsServices.Length != 1) {
			throw new InvalidDataException($"Expected one Microsoft wsService element in '{innerPath}', found {wsServices.Length}.");
		}
		bool innerChanged = !string.Equals(wsServices[0].GetAttribute("encrypted"), "true", StringComparison.OrdinalIgnoreCase);
		wsServices[0].SetAttribute("encrypted", "true");

		if (rootChanged) {
			WriteAtomically(rootPath, root);
		}
		if (innerChanged) {
			WriteAtomically(innerPath, inner);
		}
	}

	private XmlDocument Load(string path) {
		if (!_fileSystem.File.Exists(path)) {
			throw new FileNotFoundException("Required .NET Framework configuration file was not found.", path);
		}
		XmlDocument document = new() { PreserveWhitespace = true };
		document.LoadXml(_fileSystem.File.ReadAllText(path));
		return document;
	}

	private static bool SetConfigSource(XmlDocument document, string elementName, string httpPath, string httpsPath) {
		XmlElement[] elements = document.SelectNodes($"//{elementName}[@{ConfigSourceAttribute}]")!
			.Cast<XmlElement>()
			.Where(element => string.Equals(element.GetAttribute(ConfigSourceAttribute), httpPath, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(element.GetAttribute(ConfigSourceAttribute), httpsPath, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (elements.Length != 1) {
			throw new InvalidDataException($"Expected one {elementName} configSource for Creatio ServiceModel, found {elements.Length}.");
		}
		if (string.Equals(elements[0].GetAttribute(ConfigSourceAttribute), httpsPath, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		elements[0].SetAttribute(ConfigSourceAttribute, httpsPath);
		return true;
	}

	private void WriteAtomically(string path, XmlDocument document) {
		string temporaryPath = path + ".clio-https-" + Guid.NewGuid().ToString("N") + ".tmp";
		try {
			using Utf8StringWriter writer = new();
			document.Save(writer);
			_fileSystem.File.WriteAllText(temporaryPath, writer.ToString(), new UTF8Encoding(false));
			_fileSystem.File.Move(temporaryPath, path, overwrite: true);
		}
		finally {
			if (_fileSystem.File.Exists(temporaryPath)) {
				_fileSystem.File.Delete(temporaryPath);
			}
		}
	}

	private sealed class Utf8StringWriter : StringWriter {
		public override Encoding Encoding => new UTF8Encoding(false);
	}
}
