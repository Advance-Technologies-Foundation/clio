#if NETSTANDARD2_0
using System.Text;

namespace System
{
	internal static class NetStandard20StringExtensions
	{
		public static string Replace(this string source, string oldValue, string? newValue, StringComparison comparisonType)
		{
			if (source is null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			if (oldValue is null)
			{
				throw new ArgumentNullException(nameof(oldValue));
			}

			if (oldValue.Length == 0)
			{
				return source;
			}

			newValue ??= string.Empty;
			var startIndex = source.IndexOf(oldValue, comparisonType);
			if (startIndex < 0)
			{
				return source;
			}

			var result = new StringBuilder(source.Length);
			var previousIndex = 0;

			while (startIndex >= 0)
			{
				result.Append(source, previousIndex, startIndex - previousIndex);
				result.Append(newValue);
				previousIndex = startIndex + oldValue.Length;
				startIndex = source.IndexOf(oldValue, previousIndex, comparisonType);
			}

			result.Append(source, previousIndex, source.Length - previousIndex);
			return result.ToString();
		}

		public static bool Contains(this string source, string value, StringComparison comparisonType)
		{
			if (source is null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			if (value is null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			return source.IndexOf(value, comparisonType) >= 0;
		}
	}
}

namespace System.Linq
{
	internal static class NetStandard20EnumerableExtensions
	{
		public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
		{
			if (source is null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			return new HashSet<TSource>(source);
		}

		public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
		{
			if (source is null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			return new HashSet<TSource>(source, comparer);
		}
	}
}
#endif
