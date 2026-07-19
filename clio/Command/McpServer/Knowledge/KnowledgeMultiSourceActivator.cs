using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using Clio.UserEnvironment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeRuntimeConfigurationProvider {
	KnowledgeConfiguration GetCurrent();
}

internal sealed class KnowledgeRuntimeConfigurationProvider : IKnowledgeRuntimeConfigurationProvider {
	private const int MaximumSettingsBytes = 8 * 1024 * 1024;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;

	public KnowledgeRuntimeConfigurationProvider(
		ISettingsRepository settingsRepository,
		IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	public KnowledgeConfiguration GetCurrent() {
		string path = _settingsRepository.AppSettingsFilePath;
		if (!_fileSystem.File.Exists(path)) {
			return _settingsRepository.GetKnowledgeConfiguration();
		}
		using Stream stream = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		if (stream.Length <= 0 || stream.Length > MaximumSettingsBytes) {
			throw new InvalidDataException("Clio appsettings is outside the supported size bounds.");
		}
		using StreamReader reader = new(
			stream,
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
			detectEncodingFromByteOrderMarks: true);
		using JsonTextReader jsonReader = new(reader) {
			DateParseHandling = DateParseHandling.None,
			MaxDepth = 64
		};
		JObject settings = JObject.Load(jsonReader);
		JToken? knowledge = settings["knowledge"];
		if (knowledge is null || knowledge.Type == JTokenType.Null) {
			return _settingsRepository.GetKnowledgeConfiguration();
		}
		KnowledgeConfiguration configuration = knowledge.ToObject<KnowledgeConfiguration>()
			?? throw new InvalidDataException("The knowledge configuration is empty.");
		return KnowledgeSourceConfigurationValidator.ValidateAndClone(configuration);
	}
}

internal sealed class KnowledgeMultiSourceActivator : IKnowledgeBundleActivator {
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeSourceInstallationStore _store;
	private readonly IKnowledgeRuntimeConfigurationProvider _configurationProvider;
	private readonly IKnowledgeTrustFingerprintService _trustFingerprintService;
	private readonly KnowledgeBundleActivationOptions _options;
	private readonly object _activationLock = new();
	private readonly Dictionary<string, string> _observed = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, FailedActivation> _failed = new(StringComparer.OrdinalIgnoreCase);

	public KnowledgeMultiSourceActivator(
		IKnowledgeBundleRuntime runtime,
		IKnowledgeSourceInstallationStore store,
		IKnowledgeRuntimeConfigurationProvider configurationProvider,
		IKnowledgeTrustFingerprintService trustFingerprintService,
		KnowledgeBundleActivationOptions options) {
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
		_trustFingerprintService = trustFingerprintService
			?? throw new ArgumentNullException(nameof(trustFingerprintService));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegative(options.FailureRetryMilliseconds);
	}

	public string? LastDiagnostic { get; private set; }

	public void EnsureActivated() {
		lock (_activationLock) {
			try {
				KnowledgeConfiguration configuration = _configurationProvider.GetCurrent();
				_runtime.SetTopicPins(configuration.TopicPins);
				HashSet<string> configuredAliases = new(configuration.Sources.Keys, StringComparer.OrdinalIgnoreCase);
				foreach (string removedAlias in _observed.Keys.Concat(_failed.Keys)
						.Where(alias => !configuredAliases.Contains(alias))
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToArray()) {
					_runtime.DeactivateLibrary(removedAlias);
					_observed.Remove(removedAlias);
					_failed.Remove(removedAlias);
				}
				List<string> diagnostics = [];
				foreach ((string alias, KnowledgeSourceConfiguration source) in configuration.Sources) {
					if (!source.Enabled) {
						_runtime.DeactivateLibrary(alias);
						_observed.Remove(alias);
						_failed.Remove(alias);
						continue;
					}
					KnowledgeSourceCurrentState? current = _store.ReadCurrent(alias, out string? markerDiagnostic);
					if (markerDiagnostic is not null) {
						diagnostics.Add(markerDiagnostic);
						_runtime.DeactivateLibrary(alias);
						_observed.Remove(alias);
						_failed.Remove(alias);
						continue;
					}
					if (current is null) {
						_runtime.DeactivateLibrary(alias);
						_observed.Remove(alias);
						_failed.Remove(alias);
						continue;
					}
					string identity = Identity(current.Active, source);
					if (_observed.TryGetValue(alias, out string? observed)
							&& string.Equals(identity, observed, StringComparison.Ordinal)) {
						continue;
					}
					if (_failed.TryGetValue(alias, out FailedActivation? failed)
							&& string.Equals(identity, failed.Identity, StringComparison.Ordinal)
							&& Environment.TickCount64 < failed.RetryAfter) {
						diagnostics.Add(failed.Diagnostic);
						continue;
					}
					if (TryActivate(alias, source, current.Active, out string? diagnostic)) {
						_observed[alias] = identity;
						_failed.Remove(alias);
						continue;
					}
					string activeFailure = diagnostic
						?? $"Knowledge source '{alias}' rejected its current generation.";
					string? previousDiagnostic = null;
					if (current.Previous is not null) {
						string previousIdentity = Identity(current.Previous, source);
						bool previousAlreadyActive = _observed.TryGetValue(alias, out observed)
							&& string.Equals(previousIdentity, observed, StringComparison.Ordinal);
						if (previousAlreadyActive
								|| TryActivate(alias, source, current.Previous, out previousDiagnostic)) {
							_observed[alias] = previousIdentity;
							RecordFailed(alias, identity, activeFailure);
							diagnostics.Add(activeFailure);
							continue;
						}
					}
					string failure = previousDiagnostic is null
						? activeFailure
						: $"{activeFailure} Previous generation also failed: {previousDiagnostic}";
					diagnostics.Add(failure);
					RecordFailed(alias, identity, failure);
					_runtime.DeactivateLibrary(alias);
					_observed.Remove(alias);
				}
				LastDiagnostic = diagnostics.Count == 0 ? null : string.Join(" ", diagnostics);
			} catch (Exception exception) when (exception is IOException
					or UnauthorizedAccessException
					or InvalidOperationException
					or InvalidDataException
					or JsonException) {
				LastDiagnostic = $"Knowledge configuration could not be refreshed: {exception.Message}";
				_runtime.Deactivate();
				_observed.Clear();
				_failed.Clear();
			}
		}
	}

	private void RecordFailed(string alias, string identity, string diagnostic) {
		_failed[alias] = new FailedActivation(
			identity,
			diagnostic,
			Environment.TickCount64 + _options.FailureRetryMilliseconds);
	}

	private bool TryActivate(
		string alias,
		KnowledgeSourceConfiguration source,
		KnowledgeSourceGenerationPointer pointer,
		out string? diagnostic) {
		if (!_store.TryReadCandidate(alias, pointer, out InstalledKnowledgeSourceCandidate? candidate,
				out diagnostic)) {
			return false;
		}
		using MemoryStream stream = new(candidate!.BundleBytes, writable: false);
		KnowledgeBundleActivationResult activation = _runtime.ActivateLibrary(
			alias,
			source.Priority,
			source.Participation,
			stream,
			pointer.LibraryVersion,
			source.LibraryId,
			candidate.ContentRoot);
		bool success = activation.Status == KnowledgeBundleActivationStatus.Activated
			&& activation.CandidateSequence == pointer.Sequence;
		diagnostic = success ? null : activation.Diagnostic;
		return success;
	}

	private string Identity(
		KnowledgeSourceGenerationPointer pointer,
		KnowledgeSourceConfiguration source) {
		string trustFingerprint = _trustFingerprintService.TryGetFingerprint(
			source.TrustedPublicKeyPath,
			out string fingerprint)
			? fingerprint
			: "unavailable";
		return $"{source.LibraryId}:{pointer.LibraryId}:{pointer.Sequence}:{pointer.BundleDigest}:"
			+ $"{source.Priority}:{source.Participation}:{source.TrustedKeyId}:"
			+ $"{source.TrustedPublicKeyPath}:{trustFingerprint}";
	}

	private sealed record FailedActivation(string Identity, string Diagnostic, long RetryAfter);
}
