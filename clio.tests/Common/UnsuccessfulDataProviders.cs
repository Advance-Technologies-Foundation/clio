using System;
using System.Collections.Generic;
using ATF.Repository;
using ATF.Repository.Providers;
using NSubstitute;

namespace Clio.Tests.Common;

/// <summary>
/// An <see cref="IDataProvider"/> that reproduces ATF.Repository's real failure shape: every response
/// carries <c>Success = false</c>, the error text, and an empty payload - and nothing throws.
/// </summary>
/// <remarks>
/// ATF's own <c>DataProviderMock</c> has no failing mode (see
/// docs/knowledge/Tests/dataprovidermock-cannot-report-a-rejected-save.md), so this double is what makes
/// the classifying decorator testable at all. The responses are NSubstitute proxies because ATF's
/// concrete response classes are internal to its assembly.
/// </remarks>
internal sealed class UnsuccessfulDataProvider : IDataProvider {

	private readonly string _errorMessage;

	internal UnsuccessfulDataProvider(string errorMessage) => _errorMessage = errorMessage;

	public IDefaultValuesResponse GetDefaultValues(string schemaName) {
		IDefaultValuesResponse response = Substitute.For<IDefaultValuesResponse>();
		response.Success.Returns(false);
		response.ErrorMessage.Returns(_errorMessage);
		response.DefaultValues.Returns(new Dictionary<string, object>());
		return response;
	}

	public IItemsResponse GetItems(ISelectQuery selectQuery) {
		IItemsResponse response = Substitute.For<IItemsResponse>();
		response.Success.Returns(false);
		response.ErrorMessage.Returns(_errorMessage);
		response.Items.Returns(new List<Dictionary<string, object>>());
		return response;
	}

	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) {
		IExecuteResponse response = Substitute.For<IExecuteResponse>();
		response.Success.Returns(false);
		response.ErrorMessage.Returns(_errorMessage);
		response.QueryResults.Returns(new List<IExecuteItemResponse>());
		return response;
	}

	//The two value-returning members have no Success flag to report through, and the real provider does
	//not catch either - a rejected read reaches the caller as the deserializer's own exception.
	public T GetSysSettingValue<T>(string sysSettingCode) => throw BuildParserFailure();

	public bool GetFeatureEnabled(string featureCode) => throw BuildParserFailure();

	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) {
		IExecuteProcessResponse response = Substitute.For<IExecuteProcessResponse>();
		response.Success.Returns(false);
		response.ErrorMessage.Returns(_errorMessage);
		return response;
	}

	private Exception BuildParserFailure() => new Newtonsoft.Json.JsonReaderException(_errorMessage);
}

/// <summary>
/// An <see cref="IDataProvider"/> whose every member throws the supplied exception, mirroring the
/// value-returning members of the real provider, which do not catch.
/// </summary>
internal sealed class ThrowingDataProvider : IDataProvider {

	private readonly Func<Exception> _exceptionFactory;

	internal ThrowingDataProvider(Func<Exception> exceptionFactory) => _exceptionFactory = exceptionFactory;

	public IDefaultValuesResponse GetDefaultValues(string schemaName) => throw _exceptionFactory();

	public IItemsResponse GetItems(ISelectQuery selectQuery) => throw _exceptionFactory();

	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => throw _exceptionFactory();

	public T GetSysSettingValue<T>(string sysSettingCode) => throw _exceptionFactory();

	public bool GetFeatureEnabled(string featureCode) => throw _exceptionFactory();

	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) => throw _exceptionFactory();
}

/// <summary>
/// An <see cref="IDataProvider"/> that reports success and hands back the payload it was given, so a
/// pass-through assertion does not need a live environment.
/// </summary>
internal sealed class SucceedingDataProvider : IDataProvider {

	private readonly List<Dictionary<string, object>> _items;

	internal SucceedingDataProvider(List<Dictionary<string, object>> items = null) =>
		_items = items ?? new List<Dictionary<string, object>>();

	public IDefaultValuesResponse GetDefaultValues(string schemaName) {
		IDefaultValuesResponse response = Substitute.For<IDefaultValuesResponse>();
		response.Success.Returns(true);
		return response;
	}

	public IItemsResponse GetItems(ISelectQuery selectQuery) {
		IItemsResponse response = Substitute.For<IItemsResponse>();
		response.Success.Returns(true);
		response.Items.Returns(_items);
		return response;
	}

	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) {
		IExecuteResponse response = Substitute.For<IExecuteResponse>();
		response.Success.Returns(true);
		return response;
	}

	public T GetSysSettingValue<T>(string sysSettingCode) => default;

	public bool GetFeatureEnabled(string featureCode) => true;

	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) {
		IExecuteProcessResponse response = Substitute.For<IExecuteProcessResponse>();
		response.Success.Returns(true);
		return response;
	}
}

/// <summary>
/// An <see cref="IDataProvider"/> that returns <see langword="null"/> instead of a response. ATF's own
/// <c>LoadDataCollection</c> guards with <c>items != null</c>, so this shape is reachable, and it must
/// not reach a command as an empty collection either.
/// </summary>
internal sealed class NullResponseDataProvider : IDataProvider {

	public IDefaultValuesResponse GetDefaultValues(string schemaName) => null;

	public IItemsResponse GetItems(ISelectQuery selectQuery) => null;

	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => null;

	public T GetSysSettingValue<T>(string sysSettingCode) => default;

	public bool GetFeatureEnabled(string featureCode) => false;

	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) => null;
}

/// <summary>
/// An <see cref="IDataProvider"/> that fails ONLY the cliogate short-circuit
/// (<see cref="IDataProvider.GetSysSettingValue{T}"/>) and delegates everything else to an inner
/// provider, so the DataService fallback in <c>SysSettingsManager.GetSysSettingValueByCode</c> can be
/// exercised with a real answering environment behind it.
/// </summary>
internal sealed class CliogateFailingDataProvider : IDataProvider {

	private readonly IDataProvider _inner;
	private readonly Func<Exception> _shortCircuitFailure;

	internal CliogateFailingDataProvider(IDataProvider inner, Func<Exception> shortCircuitFailure) {
		_inner = inner;
		_shortCircuitFailure = shortCircuitFailure;
	}

	public IDefaultValuesResponse GetDefaultValues(string schemaName) => _inner.GetDefaultValues(schemaName);

	public IItemsResponse GetItems(ISelectQuery selectQuery) => _inner.GetItems(selectQuery);

	public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => _inner.BatchExecute(queries);

	public T GetSysSettingValue<T>(string sysSettingCode) => throw _shortCircuitFailure();

	public bool GetFeatureEnabled(string featureCode) => _inner.GetFeatureEnabled(featureCode);

	public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
		_inner.ExecuteProcess(request);
}

