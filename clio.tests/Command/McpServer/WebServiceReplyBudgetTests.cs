using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebServiceReplyBudgetTests {

	private static WebServiceParameterView Parameter(string code) =>
		new(code, code, "query", code.ToLowerInvariant(), "Text", false, false, null, null, false, []);

	// A method of about 110 bytes per parameter; 200 parameters pass the reply budget on their own.
	private static WebServiceMethodView Method(string code, int parameters = 2) =>
		new(code, code, "GET", code.ToLowerInvariant(), 30_000, "json", true,
			[.. Enumerable.Range(0, parameters).Select(i => Parameter($"{code}QueryParameter{i:000}"))], []);

	private static WebServiceToolResult Reply(IEnumerable<WebServiceMethodView> methods,
		IReadOnlyList<WebServiceUsage> usages = null, IReadOnlyList<string> notices = null) =>
		new(true, Service: new WebServiceView(Guid.Empty, "UsrSvc", "UsrSvc", "Custom", "REST", "http://x.example", 0,
			new WebServiceAuthView("none", null, null, null, false), methods.ToList()), Usages: usages, Notices: notices);

	private static IReadOnlyList<WebServiceUsage> Usages(int count, string method = "UsrM00") =>
		[.. Enumerable.Range(0, count).Select(i =>
			new WebServiceUsage($"UsrCallerProcess{i:000}", $"Caller process {i:000}", "Custom", $"WebService{i:000}", method))];

	private static void ShouldFit<T>(T reply) =>
		McpResultSize.Of(reply).Should().BeLessThanOrEqualTo(McpResultSize.InlineReplyBudgetBytes,
			because: "a reply over the budget is written to a file and read back in pieces");

	[Test]
	[Description("A method that no longer fits after the run of methods that do is skipped, and so is a second one, while a small method after them is still shown.")]
	public void Fit_Should_Keep_Trying_After_A_Method_That_Does_Not_Fit() {
		// Arrange
		WebServiceToolResult reply = Reply([Method("UsrM00"), Method("UsrM01"), Method("UsrM02"),
			Method("UsrZBig1", 200), Method("UsrZBig2", 200), Method("UsrZSmall")]);

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.Service.Methods.Select(m => m.Code).Should().Equal(["UsrM00", "UsrM01", "UsrM02", "UsrZSmall"],
			because: "two large methods in a row do not stop the small one after them from being shown");
		result.Service.MethodsNotShown.Select(m => m.Code).Should().Equal(["UsrZBig1", "UsrZBig2"],
			because: "the methods that do not fit are listed by code");
	}

	[TestCase(20, true)]
	[TestCase(21, false)]
	[Description("The search for methods that still fit stops after twenty misses in a row, which bounds the work on a service of many large methods.")]
	public void Fit_Should_Stop_Trying_After_Twenty_Misses_In_A_Row(int largeMethods, bool lastShown) {
		// Arrange
		WebServiceToolResult reply = Reply([Method("UsrA"),
			.. Enumerable.Range(0, largeMethods).Select(i => Method($"UsrBig{i:00}", 200)), Method("UsrLast")]);

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.Service.Methods.Any(m => m.Code == "UsrLast").Should().Be(lastShown,
			because: "the first large method ends the run and is not retried, so the misses after it are counted up to twenty");
	}

	[Test]
	[Description("With nothing fitting even by code, the reply lists every method by code and returns at once.")]
	public void Fit_Should_List_Every_Method_By_Code_When_Even_The_Codes_Pass_The_Budget() {
		// Arrange
		WebServiceToolResult reply = Reply(Enumerable.Range(0, 2_000).Select(i => Method($"UsrMethodWithALongerCode{i:0000}")));

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		result.Service.Methods.Should().BeEmpty(because: "no method fits in full when the codes alone pass the budget");
		result.Service.MethodsNotShown.Should().HaveCount(2_000, because: "every method is still named by code");
	}

	[Test]
	[Description("Usages that do not fit are cut last, as a prefix, with a notice that counts them, while the methods stay in full.")]
	public void Fit_Should_Cut_The_Usages_Last() {
		// Arrange
		WebServiceToolResult reply = Reply(Enumerable.Range(0, 5).Select(i => Method($"UsrM{i:00}")), Usages(150));

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.Service.Methods.Should().HaveCount(5, because: "the methods fit once the usages are cut");
		result.Service.MethodsNotShown.Should().BeNull(because: "no method had to be listed by code");
		result.Usages.Should().HaveCountGreaterThan(0).And.HaveCountLessThan(150, because: "the usages fill the room left");
		result.Usages.Should().Equal(reply.Usages.Take(result.Usages.Count), because: "the usages kept are the first ones");
		result.Notices.Should().ContainSingle(n => n.StartsWith($"{150 - result.Usages.Count} of the 150 usages are not listed"),
			because: "the agent is told how many usages are left out");
	}

	[Test]
	[Description("Usages that alone fill the budget do not push out a method a modify call built: it stays in full and the usages are cut.")]
	public void Fit_Should_Keep_The_Methods_A_Change_Built_Over_The_Usages() {
		// Arrange
		WebServiceToolResult reply = Reply(Enumerable.Range(0, 20).Select(i => Method($"UsrM{i:00}", 10)), Usages(200, "UsrM15"));

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply, ["UsrM15"]);

		// Assert
		ShouldFit(result);
		result.Service.Methods.Should().Contain(m => m.Code == "UsrM15", because: "the method the call changed is shown in full first");
		result.Usages.Count.Should().BeLessThan(200, because: "the acknowledged usages give way to the changed method");
	}

	[Test]
	[Description("A single method stays in full and its usages are cut, with a notice that says no narrower read lists more of them.")]
	public void Fit_Should_Cut_The_Usages_Of_A_Single_Method() {
		// Arrange
		WebServiceToolResult reply = Reply([Method("UsrM00", 20)], Usages(200));

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.Service.Methods.Should().ContainSingle(because: "a single method is always returned in full");
		result.Notices.Should().ContainSingle(n => n.Contains("usages are not listed") && n.Contains("for a single method"),
			because: "the notice does not send the agent back to the same call");
	}

	[Test]
	[Description("Notices that alone fill the budget are cut, first ones kept, with a count; the methods still get the room left.")]
	public void Fit_Should_Cut_The_Notices_When_They_Alone_Pass_The_Budget() {
		// Arrange
		List<string> notices = [.. Enumerable.Range(0, 80).Select(i =>
			$"method 'UsrM{i:00}': response parameter 'UsrValue' has the JSONPath '$.value', but the response is XML, which "
			+ "the platform reads by element path; this path reads nothing.")];
		WebServiceToolResult reply = Reply(Enumerable.Range(0, 80).Select(i => Method($"UsrM{i:00}")), notices: notices);

		// Act
		WebServiceToolResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.Notices[0].Should().Be(notices[0], because: "the first notices are kept");
		result.Notices.Should().Contain(n => n.Contains("notices are left out to keep this reply inline"),
			because: "the notices left out are counted");
		result.Notices.Should().Contain(n => n.Contains("methods-not-shown"), because: "the narrowing notice is never cut");
	}

	[Test]
	[Description("A body the budget alone cuts is marked as cut, and a cut never ends in half a surrogate pair.")]
	public void Fit_Should_Mark_A_Body_It_Cuts_And_Keep_Surrogate_Pairs_Whole() {
		// Arrange
		var reply = new WebServiceTestResult(true, StatusCode: 200, ResponseBody: string.Concat(Enumerable.Repeat("\U0001F600", 9_000)));

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.ResponseBodyTruncated.Should().BeTrue(because: "the body under the designer's character limit was cut for the budget");
		result.ResponseBody.Length.Should().BeLessThan(reply.ResponseBody.Length, because: "the body was cut");
		char.IsHighSurrogate(result.ResponseBody[^1]).Should().BeFalse(because: "a cut never splits a surrogate pair");
	}

	[Test]
	[Description("A platform error too long for the reply is cut to its start, with a note, and the reply fits.")]
	public void Fit_Should_Cut_A_Long_Error() {
		// Arrange
		var reply = new WebServiceTestResult(false, "Bad gateway. " + new string('x', 40_000), 502);

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		ShouldFit(result);
		result.Error.Should().StartWith("Bad gateway.", because: "the cause is at the start of the error");
		result.Error.Should().EndWith("(the rest of the error is cut to keep this reply inline)",
			because: "the agent is told the error was cut");
		result.Error.Length.Should().BeLessThan(2_100, because: "the error keeps at most its share");
	}

	[Test]
	[Description("A short error is returned as it is.")]
	public void Fit_Should_Keep_A_Short_Error() {
		// Arrange
		var reply = new WebServiceTestResult(false, "Bad gateway.", 502);

		// Act
		WebServiceTestResult result = WebServiceReplyBudget.Fit(reply);

		// Assert
		result.Should().BeSameAs(reply, because: "a reply that fits is not changed");
	}
}
