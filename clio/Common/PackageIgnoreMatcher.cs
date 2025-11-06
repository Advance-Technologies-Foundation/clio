using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Clio.Common
{
    /// <summary>
    /// Utility for matching package names with masks from IgnorePackages.
    /// </summary>
    public static class PackageIgnoreMatcher
    {
        /// <summary>
        /// Cache for compiled regex patterns to improve performance.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Regex> _regexCache = new ConcurrentDictionary<string, Regex>();

        /// <summary>
        /// Checks if a package should be ignored by the list of masks.
        /// </summary>
        /// <param name="packageName">Package name.</param>
        /// <param name="ignoreMasks">List of masks/names to ignore.</param>
        /// <returns>true if the package should be ignored.</returns>
        public static bool IsIgnored(string packageName, IEnumerable<string> ignoreMasks)
        {
            foreach (var mask in ignoreMasks)
            {
                if (IsMatch(packageName, mask))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the package name matches the mask (supports * and ?).
        /// </summary>
        private static bool IsMatch(string input, string mask)
        {
            var regex = _regexCache.GetOrAdd(mask, m =>
            {
                var pattern = "^" + Regex.Escape(m).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            });
            return regex.IsMatch(input);
        }
    }
}
