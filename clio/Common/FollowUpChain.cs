using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;
using ErrorOr;
using Terrasoft.Core.DB;

namespace Clio.Common;

public interface IFollowUpChain {
	public IExecutableChain With(IFollowupUpChainItem item);
	
	public IDictionary<string, object> CreateContextFromOptions(object options);
}

public interface IFollowupUpChainItem {
	public ErrorOr<int> Execute();
	public ErrorOr<int> Execute(IDictionary<string, object> context);
}

public interface IExecutableChain{
	public IExecutableChain With(IFollowupUpChainItem item);
	public ErrorOr<int> Execute();
	public ErrorOr<int> Execute(IDictionary<string, object> context);
}

public class FollowUpChain : IFollowUpChain, IExecutableChain{
	private readonly List<IFollowupUpChainItem> _chainItems = [];

	public IExecutableChain With(IFollowupUpChainItem item) {
		_chainItems.Add(item);
		return this;
	}

	public IDictionary<string, object> CreateContextFromOptions(object options) {
		Dictionary<string, object> context = new();
		var props = options.GetType()
			   .GetProperties(BindingFlags.Public | BindingFlags.Instance)
			   .Where(p => p.GetCustomAttribute<ValueAttribute>() != null ||
						   p.GetCustomAttribute<OptionAttribute>() != null)
			   .Select(p => new { p.Name, pValue = p.GetValue(options) })
			   .ToList();
		
		foreach (var prop in props.Where(prop => prop.pValue != null)) {
			context[prop.Name] = prop.pValue;
		}
		return context;
	}

	public ErrorOr<int> Execute() {
		foreach (IFollowupUpChainItem item in _chainItems) {
			ErrorOr<int> result = item.Execute();
			if (result.IsError) {
				return result;
			}
		}
		return 0;
	}
	
	public ErrorOr<int> Execute(IDictionary<string, object> context) {
		foreach (IFollowupUpChainItem item in _chainItems) {
			ErrorOr<int> result = item.Execute(context);
			if (result.IsError) {
				return result;
			}
		}
		return 0;
	}
}
