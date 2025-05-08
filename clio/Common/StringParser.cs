﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common;

public class StringParser
{

    #region Methods: Public

    public static IEnumerable<string> ParseArray(string input)
    {
        return input
               .Split(',', StringSplitOptions.RemoveEmptyEntries)
               .Select(p => p.Trim())
               .ToList();
    }

    #endregion

}
