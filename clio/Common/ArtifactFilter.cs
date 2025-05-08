using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common;

public class ArtifactFilter
{

    #region Fields: Private

    private readonly string _baseDirectory;

    #endregion

    #region Fields: Internal

    internal static readonly Func<string, IEnumerable<string>> GetDirectories = baseDirectory =>
        Directory.GetDirectories(baseDirectory)
                 .Select(item => new Uri(item, UriKind.Absolute).Segments.Last())
                 .Where(item => item == baseDirectory);

    #endregion

    #region Constructors: Public

    public ArtifactFilter(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    #endregion

}
