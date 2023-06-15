namespace Clio.YAML;

using System;
using System.Collections.Generic;
using System.Linq
using Clio.Command;

public class Experement
{

	public string FunctionOne() {
		
		return "OK";
	} 

	private  string FunctionTwo() {
		var x = new[] {1,2,3};
		var sqr = x.MySelect(x=>x*x);
		return MyFunctions.FuncOne();
	}
	
}




public static class MyFunctions
{

	public static readonly Func<string> FuncOne = () => "OK";
	
	
	
	public static IEnumerable<TResult> MySelect<T, TResult>(this IEnumerable<T> xs, Func<T, TResult> f)
	{
		foreach (var x in xs) yield return f(x);
	}

}


