using System;
using NUnit.Framework;

namespace {{packageName}}.IntegrationTests.Infrastructure;

public sealed class CreatioTestSettings {
	private CreatioTestSettings(Uri url, bool isNetCore, string username, string password, string accessToken) {
		Url = url;
		IsNetCore = isNetCore;
		Username = username;
		Password = password;
		AccessToken = accessToken;
	}

	public Uri Url { get; }
	public bool IsNetCore { get; }
	public string Username { get; }
	public string Password { get; }
	public string AccessToken { get; }
	public bool UsesAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
	public override string ToString() => $"Creatio URL: {Url}; IsNetCore: {IsNetCore}; Authentication: {(UsesAccessToken ? "access token" : "username/password")}";

	public static CreatioTestSettings Load() {
		string Read(string name) => TestContext.Parameters.Get(name, Environment.GetEnvironmentVariable(name));
		string url = Read("CREATIO_URL");
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri applicationUrl)) {
			throw new InvalidOperationException("Set CREATIO_URL to an absolute Creatio application URL.");
		}
		if (!bool.TryParse(Read("CREATIO_IS_NETCORE"), out bool isNetCore)) {
			throw new InvalidOperationException("Set CREATIO_IS_NETCORE to true or false.");
		}
		string token = Read("CREATIO_ACCESS_TOKEN");
		string username = Read("CREATIO_USERNAME");
		string password = Read("CREATIO_PASSWORD");
		bool hasToken = !string.IsNullOrWhiteSpace(token);
		bool hasPassword = !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
		if (hasToken == hasPassword) {
			throw new InvalidOperationException(
				"Configure exactly one authentication mode: CREATIO_ACCESS_TOKEN or CREATIO_USERNAME and CREATIO_PASSWORD.");
		}
		return new CreatioTestSettings(applicationUrl, isNetCore, username, password, token);
	}
}
