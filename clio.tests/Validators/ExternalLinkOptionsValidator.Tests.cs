using Clio.Command;
using Clio.Requests.Validators;
using FluentValidation;
using NUnit.Framework;
using System;
using System.Linq;

namespace Clio.Tests.Validators
{
	public class ExternalLinkOptionsValidatorTestCase
	{

		private ExternalLinkOptionsValidator _sut;

		[SetUp]
		public void Init()
		{
			_sut = new ExternalLinkOptionsValidator();
		}

		[Test, Category("Unit")]
		[TestCase("clio://IISScannerRequest/?return=count")]
		//[TestCase("clio://IISScanner/?return=details")]
		public void ExternalLinkOptionsValidator_ShouldValidate_As_Valid(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,

			};
			//Act
			var validationResults = _sut.Validate(request);
			Assert.IsTrue(validationResults.IsValid);
		}

		[Test, Category("Unit")]
		[TestCase("random_string")]
		[TestCase("more random text")]
		[TestCase("clio://IISScannerRequest /?return=count")]
		[TestCase("clio:// IISScannerRequest/?return=count")]
		[TestCase("clio:// IISScannerRequest /?return=count")]
		[TestCase("clio://  /?return=count")]
		public void ExternalLinkOptionsValidator_ShouldValidate_As_InValid_NotAUri(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};
			var expected = new
			{
				ErrorCode = "10",
				ErrorMessage = "Value is not in the correct format",
				Severity = Severity.Error,
				AttemptedValue = content
			};
			//Act
			var validationResults = _sut.Validate(request);
			Assert.Multiple(() =>
			{
				Assert.IsFalse(validationResults.IsValid);
				Assert.AreEqual(1, validationResults.Errors.Count);
				Assert.AreEqual(expected.Severity, validationResults.Errors.FirstOrDefault().Severity);
				Assert.AreEqual(expected.ErrorMessage, validationResults.Errors.FirstOrDefault().ErrorMessage);
				Assert.AreEqual(expected.ErrorCode, validationResults.Errors.FirstOrDefault().ErrorCode);
				Assert.AreEqual(expected.AttemptedValue, validationResults.Errors.FirstOrDefault().AttemptedValue);
			});
		}


		[Test, Category("Unit")]
		[TestCase("")]
		[TestCase(" ")]
		[TestCase(null)]
		public void ExternalLinlValidator_ShouldValidate_As_InValid_EmptyContent(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};
			//Act
			var validationResults = _sut.Validate(request);

			Assert.Multiple(() =>
			{
				Assert.IsFalse(validationResults.IsValid);
			});

		}


		[Test, Category("Unit")]
		[TestCase("c://IISScannerRequest/?return=count")]
		[TestCase("cl://IISScannerRequest/?return=count")]
		[TestCase("cli://IISScannerRequest/?return=count")]
		[TestCase("cli://")]
		public void ExternalLinlValidator_ShouldValidate_As_InValid_WhenDoesNotStartWithClio(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};


			var expected = new
			{
				ErrorCode = "20",
				ErrorMessage = "Value has to start with clio://",
				Severity = Severity.Error,
				AttemptedValue = content
			};
			//Act
			var validationResults = _sut.Validate(request);

			Assert.Multiple(() =>
			{
				Assert.IsFalse(validationResults.IsValid);
				Assert.AreEqual(1, validationResults.Errors.Count);
				Assert.AreEqual(expected.Severity, validationResults.Errors.FirstOrDefault().Severity);
				Assert.AreEqual(expected.ErrorMessage, validationResults.Errors.FirstOrDefault().ErrorMessage);
				Assert.AreEqual(expected.ErrorCode, validationResults.Errors.FirstOrDefault().ErrorCode);
				Assert.AreEqual(expected.AttemptedValue, validationResults.Errors.FirstOrDefault().AttemptedValue);
			});

		}


		[Test, Category("Unit")]
		[TestCase("clio://randomCommand/?return=count")]
		[TestCase("clio://345634/?return=count")]
		public void ExternalLinlValidator_ShouldValidate_As_InValid_WhenCommandNotFound(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};

			Uri.TryCreate(content, UriKind.Absolute, out Uri _uriFromString);
			string commandName = _uriFromString?.Host;
			var expected = new
			{
				ErrorCode = "50",
				ErrorMessage = $"Command <{commandName}> not found",
				Severity = Severity.Error,
				AttemptedValue = commandName
			};
			//Act
			var validationResults = _sut.Validate(request);

			Assert.Multiple(() =>
			{
				Assert.IsFalse(validationResults.IsValid);
				Assert.AreEqual(1, validationResults.Errors.Count);
				Assert.AreEqual(expected.Severity, validationResults.Errors.FirstOrDefault().Severity);
				Assert.AreEqual(expected.ErrorMessage, validationResults.Errors.FirstOrDefault().ErrorMessage);
				Assert.AreEqual(expected.ErrorCode, validationResults.Errors.FirstOrDefault().ErrorCode);
				Assert.AreEqual(expected.AttemptedValue, validationResults.Errors.FirstOrDefault().AttemptedValue);
			});
		}


		[Test, Category("Unit")]
		[TestCase("clio://IISScannerRequest/?return=count")]
		[TestCase("clio://Restart/?return=count")]
		[TestCase("clio://OpenUrl/?return=count")]
		public void ExternalLinlValidator_ShouldValidate_As_Valid_WhenCommandFound(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};

			Uri.TryCreate(content, UriKind.Absolute, out Uri _uriFromString);
			string commandName = _uriFromString?.Host;
			var expected = new
			{
				ErrorCode = "50",
				ErrorMessage = $"Command <{commandName}> not found",
				Severity = Severity.Error,
				AttemptedValue = commandName
			};
			//Act
			var validationResults = _sut.Validate(request);
			Assert.Multiple(() =>
			{
				Assert.IsTrue(validationResults.IsValid);
			});
		}


		[Test, Category("Unit")]
		[TestCase("clio://IISScannerRequest/?return=", "return", " ")]
		[TestCase("clio://IISScannerRequest/?=count", " ", "count")]
		public void ExternalLinlValidator_ShouldValidate_As_InValid_QueryIsWrong(string content, string key, string val)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};

			Uri.TryCreate(content, UriKind.Absolute, out Uri _uriFromString);
			string commandName = _uriFromString?.Host;
			var expected = new
			{
				ErrorCode = "50",
				ErrorMessage = $"Query not in correct format key is '{key}' when value '{val}'",
				Severity = Severity.Error,
				AttemptedValue = _uriFromString
			};
			//Act
			var validationResults = _sut.Validate(request);

			Assert.Multiple(() =>
			{
				Assert.IsFalse(validationResults.IsValid);
				Assert.AreEqual(1, validationResults.Errors.Count);
				Assert.AreEqual(expected.Severity, validationResults.Errors.FirstOrDefault().Severity);
				Assert.That(validationResults.Errors.FirstOrDefault().ErrorMessage.StartsWith("Query not in correct format key is"));
				Assert.AreEqual(expected.ErrorCode, validationResults.Errors.FirstOrDefault().ErrorCode);
				Assert.AreEqual(expected.AttemptedValue, validationResults.Errors.FirstOrDefault().AttemptedValue);
			});
		}

		[Test, Category("Unit")]
		[TestCase("clio://IISScannerRequest/?return=count")]
		[TestCase("clio://IISScannerRequest/?return=count&a=b")]
		[TestCase("clio://IISScannerRequest/?return=count&a=b&c=d")]
		[TestCase("clio://IISScannerRequest/?return=count&a=b&c=d&c=d,d")]
		public void ExternalLinlValidator_ShouldValidate_As_Valid_QueryIsCorrect(string content)
		{
			//Arange 
			var request = new ExternalLinkOptions()
			{
				Content = content,
			};

			//Act
			var validationResults = _sut.Validate(request);

			Assert.Multiple(() =>
			{
				Assert.IsTrue(validationResults.IsValid);
			});
		}

	}
}

