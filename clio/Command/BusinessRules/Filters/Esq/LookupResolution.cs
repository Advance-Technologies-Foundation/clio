using System;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Result of resolving a Lookup filter value (either by display name or by GUID) on a lookup's
/// reference schema. Carries both the record Id and its primary-display-column value so the
/// converter can emit the full Freedom UI lookup parameter shape
/// (<c>{Name, Id, value, displayValue}</c>) without losing the display name we already paid a
/// SelectQuery round-trip to obtain. <see cref="DisplayValue"/> may be null when the caller
/// supplied a GUID and no resolver round-trip was performed.
/// </summary>
internal sealed record LookupResolution(Guid Id, string? DisplayValue);
