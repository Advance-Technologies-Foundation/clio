using System.Xml;
using System.Xml.Linq;

namespace Creatio.ConflictResolver;

internal static class SafeXmlDocumentParser
{
	private const long MaxDocumentCharacters = 4 * 1024 * 1024;

	public static XDocument Parse(string content)
	{
		var settings = new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			MaxCharactersInDocument = MaxDocumentCharacters
		};
		using var textReader = new StringReader(content);
		using var xmlReader = XmlReader.Create(textReader, settings);
		return XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
	}
}
