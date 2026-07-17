using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Scope-aware business-rule attribute map for a page. Root-scope operands key on their plain
/// attribute name (surfaced datasource-bound attributes plus unbound/technical page-local attributes);
/// scoped operands key on <c>scopeId::path</c> and resolve either a page parameter
/// (<see cref="BusinessRuleConstants.PageParametersScope"/>) or a column of a DataSource entity schema
/// (looked up lazily, so a column that is not surfaced on the page is still reachable).
/// </summary>
/// <remarks>
/// DataSource column resolution reuses <see cref="IEntityBusinessRuleAttributeProvider"/>, so a scoped
/// path may itself be a forward reference within the datasource entity (for example
/// <c>PDS::Contact.Account</c>). Enumeration and <see cref="Keys"/> expose the bounded root and page
/// parameter entries only; the unbounded per-DataSource column space is resolved on demand through
/// <see cref="TryGetValue"/>.
/// </remarks>
internal sealed class PageScopedBusinessRuleAttributeMap
	: IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> {

	private readonly IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> _rootAttributes;
	private readonly IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> _parameters;
	private readonly IReadOnlyDictionary<string, string> _dataSourceEntitySchemas;
	private readonly IEntityBusinessRuleAttributeProvider _entityAttributeProvider;
	private readonly Guid _packageUId;
	private readonly Dictionary<string, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> _entityColumnMaps =
		new(StringComparer.Ordinal);

	public PageScopedBusinessRuleAttributeMap(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> rootAttributes,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> parameters,
		IReadOnlyDictionary<string, string> dataSourceEntitySchemas,
		IEntityBusinessRuleAttributeProvider entityAttributeProvider,
		Guid packageUId) {
		_rootAttributes = rootAttributes;
		_parameters = parameters;
		_dataSourceEntitySchemas = dataSourceEntitySchemas;
		_entityAttributeProvider = entityAttributeProvider;
		_packageUId = packageUId;
	}

	/// <summary>Gets the datasource scope names available on the page (from <c>modelConfig.dataSources</c>).</summary>
	public IEnumerable<string> DataSourceScopes => _dataSourceEntitySchemas.Keys;

	/// <summary>Determines whether <paramref name="scopeId"/> is a resolvable scope for this page.</summary>
	public bool IsKnownScope(string? scopeId) =>
		string.IsNullOrEmpty(scopeId)
		|| string.Equals(scopeId, PageParametersScope, StringComparison.Ordinal)
		|| _dataSourceEntitySchemas.ContainsKey(scopeId);

	public IEnumerable<string> Keys =>
		_rootAttributes.Keys.Concat(_parameters.Keys.Select(name => BuildScopedOperandKey(PageParametersScope, name)));

	public IEnumerable<BusinessRuleAttributeDescriptor> Values =>
		this.Select(pair => pair.Value);

	public int Count => _rootAttributes.Count + _parameters.Count;

	public BusinessRuleAttributeDescriptor this[string key] =>
		TryGetValue(key, out BusinessRuleAttributeDescriptor? value)
			? value
			: throw new KeyNotFoundException($"Attribute '{key}' was not found.");

	public bool ContainsKey(string key) => TryGetValue(key, out _);

	public bool TryGetValue(string key, out BusinessRuleAttributeDescriptor value) {
		value = default!;
		if (string.IsNullOrEmpty(key)) {
			return false;
		}

		int separatorIndex = key.IndexOf(ScopedOperandKeySeparator, StringComparison.Ordinal);
		if (separatorIndex < 0) {
			return _rootAttributes.TryGetValue(key, out value!);
		}

		string scopeId = key[..separatorIndex];
		string path = key[(separatorIndex + ScopedOperandKeySeparator.Length)..];
		if (string.IsNullOrEmpty(path)) {
			return false;
		}

		if (string.Equals(scopeId, PageParametersScope, StringComparison.Ordinal)) {
			return _parameters.TryGetValue(path, out value!);
		}

		return TryResolveDataSourceColumn(scopeId, path, out value);
	}

	private bool TryResolveDataSourceColumn(string scopeId, string path, out BusinessRuleAttributeDescriptor value) {
		value = default!;
		if (!_dataSourceEntitySchemas.TryGetValue(scopeId, out string? entitySchemaName)
			|| string.IsNullOrWhiteSpace(entitySchemaName)) {
			return false;
		}

		try {
			IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> columns = GetEntityColumns(entitySchemaName);
			return columns.TryGetValue(path, out value!);
		} catch (InvalidOperationException) {
			// A datasource whose entity schema (or an intermediate forward-reference schema) cannot be
			// fetched resolves no operand, surfacing as a clean "Unknown attribute in scope" validation
			// error rather than escaping the page validator's ArgumentException handler. Mirrors
			// PageBusinessRuleAttributeProvider.TryGetSupportedAttribute.
			value = default!;
			return false;
		}
	}

	private IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetEntityColumns(string entitySchemaName) {
		if (!_entityColumnMaps.TryGetValue(entitySchemaName, out IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>? columns)) {
			columns = _entityAttributeProvider.GetAttributes(entitySchemaName, _packageUId).Attributes;
			_entityColumnMaps[entitySchemaName] = columns;
		}

		return columns;
	}

	public IEnumerator<KeyValuePair<string, BusinessRuleAttributeDescriptor>> GetEnumerator() {
		foreach (KeyValuePair<string, BusinessRuleAttributeDescriptor> pair in _rootAttributes) {
			yield return pair;
		}

		foreach (KeyValuePair<string, BusinessRuleAttributeDescriptor> pair in _parameters) {
			yield return new KeyValuePair<string, BusinessRuleAttributeDescriptor>(
				BuildScopedOperandKey(PageParametersScope, pair.Key),
				pair.Value);
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
