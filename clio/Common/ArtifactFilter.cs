using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using DocumentFormat.OpenXml.Spreadsheet;

namespace Clio.Common;
public class ArtifactFilter(string baseDirectory)
{
    private readonly string _baseDirectory = baseDirectory;
    internal static readonly Func<string, IEnumerable<string>> GetDirectories = baseDirectory =>
        Directory.GetDirectories(baseDirectory)
            .Select(item => new Uri(item, UriKind.Absolute).Segments.Last())
            .Where(item => item == baseDirectory);
}
