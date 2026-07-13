using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Platform;

namespace ClioRing.Resources;

public static class IconGeometries
{
	public static Geometry NetCore { get; } = LoadGeometry("avares://ClioRing/Assets/Icons/dotnet.svg");
	public static Geometry NetFramework { get; } = LoadGeometry("avares://ClioRing/Assets/Icons/net-framework.svg");
	public static Geometry GitPull { get; } = LoadGeometry("avares://ClioRing/Assets/Icons/gui-git-pull-request.svg");
	public static Geometry VsCode { get; } = LoadGeometry("avares://ClioRing/Assets/Icons/visual-studio-code-logo.svg");

	private static Geometry LoadGeometry(string assetUri)
	{
		using Stream stream = AssetLoader.Open(new Uri(assetUri));
		XDocument svg = XDocument.Load(stream);

		IEnumerable<string> paths = svg
			.Descendants()
			.Where(node => node.Name.LocalName == "path")
			.Select(node => node.Attribute("d")?.Value ?? string.Empty)
			.Where(data => !string.IsNullOrWhiteSpace(data));

		string geometryData = string.Join(" ", paths);
		return string.IsNullOrWhiteSpace(geometryData)
			? Geometry.Parse("M0,0")
			: Geometry.Parse(geometryData);
	}
}
