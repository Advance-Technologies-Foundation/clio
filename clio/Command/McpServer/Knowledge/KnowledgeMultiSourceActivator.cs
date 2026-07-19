using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading;
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
	private readonly object _cacheLock = new();
	private string? _cachedPath;
	private DateTime _cachedLastWriteUtc;
	private long _cachedLength;
	private KnowledgeConfiguration? _cachedConfiguration;

	public KnowledgeRuntimeConfigurationProvider(
		ISettingsRepository settingsRepository,
		IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	public KnowledgeConfiguration GetCurrent() {
		lock (_cacheLock) {
			string path = _settingsRepository.AppSettingsFilePath;
			if (!_fileSystem.File.Exists(path)) {
				return _settingsRepository.GetKnowledgeConfiguration();
			}
			IFileInfo before = _fileSystem.FileInfo.New(path);
			if (_cachedConfiguration is not null
					&& string.Equals(path, _cachedPath, StringComparison.Ordinal)
					&& before.Length == _cachedLength
					&& before.LastWriteTimeUtc == _cachedLastWriteUtc) {
				return _cachedConfiguration;
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
			KnowledgeConfiguration configuration = knowledge is null || knowledge.Type == JTokenType.Null
				? _settingsRepository.GetKnowledgeConfiguration()
				: KnowledgeSourceConfigurationValidator.ValidateAndClone(
					knowledge.ToObject<KnowledgeConfiguration>()
					?? throw new InvalidDataException("The knowledge configuration is empty."));
			IFileInfo after = _fileSystem.FileInfo.New(path);
			if (before.Length == after.Length && before.LastWriteTimeUtc == after.LastWriteTimeUtc) {
				_cachedPath = path;
				_cachedLength = after.Length;
				_cachedLastWriteUtc = after.LastWriteTimeUtc;
				_cachedConfiguration = configuration;
			}
			return configuration;
		}
	}
}

internal sealed class KnowledgeMultiSourceActivator : IKnowledgeBundleActivator {
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeSourceInstallationStore _store;
	private readonly IKnowledgeRuntimeConfigurationProvider _configurationProvider;
	private readonly IKnowledgeGitRepositoryReader _gitReader;
	private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeRepositoryTransport> _repositoryTransports;
	private readonly IFileSystem _fileSystem;
	private readonly IKnowledgeTrustFingerprintService _trustFingerprintService;
	private readonly KnowledgeBundleActivationOptions _options;
	private readonly object _activationLock = new();
	private readonly Dictionary<string, string> _observed = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _observedGitConfiguration = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, FailedActivation> _failed = new(StringComparer.OrdinalIgnoreCase);
	private IReadOnlyDictionary<string, string> _observedTopicPins = new Dictionary<string, string>(
		StringComparer.Ordinal);

	public KnowledgeMultiSourceActivator(
		IKnowledgeBundleRuntime runtime,
		IKnowledgeSourceInstallationStore store,
		IKnowledgeRuntimeConfigurationProvider configurationProvider,
		IKnowledgeGitRepositoryReader gitReader,
		IEnumerable<IKnowledgeRepositoryTransport> repositoryTransports,
		IFileSystem fileSystem,
		IKnowledgeTrustFingerprintService trustFingerprintService,
		KnowledgeBundleActivationOptions options) {
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
		_gitReader = gitReader ?? throw new ArgumentNullException(nameof(gitReader));
		ArgumentNullException.ThrowIfNull(repositoryTransports);
		_repositoryTransports = repositoryTransports.ToDictionary(
			transport => transport.Type,
			transport => transport);
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_trustFingerprintService = trustFingerprintService
			?? throw new ArgumentNullException(nameof(trustFingerprintService));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegative(options.FailureRetryMilliseconds);
	}

	public string? LastDiagnostic { get; private set; }

	public void EnsureActivated() {
		if (!Monitor.TryEnter(_activationLock)) {
			return;
		}
		try {
			try {
				KnowledgeConfiguration configuration = _configurationProvider.GetCurrent();
				if (!TopicPinsEqual(_observedTopicPins, configuration.TopicPins)) {
					_runtime.SetTopicPins(configuration.TopicPins);
					_observedTopicPins = new Dictionary<string, string>(configuration.TopicPins, StringComparer.Ordinal);
				}
				HashSet<string> configuredAliases = new(configuration.Sources.Keys, StringComparer.OrdinalIgnoreCase);
				foreach (string removedAlias in _observed.Keys.Concat(_failed.Keys)
						.Where(alias => !configuredAliases.Contains(alias))
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToArray()) {
					_runtime.DeactivateLibrary(removedAlias);
					_observed.Remove(removedAlias);
					_observedGitConfiguration.Remove(removedAlias);
					_failed.Remove(removedAlias);
				}
				List<string> diagnostics = [];
				foreach ((string alias, KnowledgeSourceConfiguration source) in configuration.Sources) {
					if (!source.Enabled) {
						_runtime.DeactivateLibrary(alias);
						_observed.Remove(alias);
						_observedGitConfiguration.Remove(alias);
						_failed.Remove(alias);
						continue;
					}
					if (_repositoryTransports.TryGetValue(source.Type, out IKnowledgeRepositoryTransport? repositoryTransport)) {
						ActivateRepository(alias, source, repositoryTransport, diagnostics);
						continue;
					}
					_observedGitConfiguration.Remove(alias);
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
				_observedGitConfiguration.Clear();
				_failed.Clear();
			}
		} finally {
			Monitor.Exit(_activationLock);
		}
	}

	private void ActivateRepository(
		string alias,
		KnowledgeSourceConfiguration source,
		IKnowledgeRepositoryTransport transport,
		ICollection<string> diagnostics) {
		string repositoryPath = _store.GetGitRepositoryPath(alias, createSourceRoot: false);
		if (!_fileSystem.Directory.Exists(repositoryPath)) {
			_runtime.DeactivateLibrary(alias);
			_observed.Remove(alias);
			_observedGitConfiguration.Remove(alias);
			_failed.Remove(alias);
			return;
		}
		try {
			string? revision = transport.GetCurrentRevision(repositoryPath);
			if (revision is null) {
				HandleGitFailure(alias, source, $"git:{source.LibraryId}:unreadable", diagnostics,
					$"Git knowledge source '{alias}' has no valid current revision.");
				return;
			}
			string identity = GitIdentity(source, revision);
			if (_observed.TryGetValue(alias, out string? observed)
					&& string.Equals(identity, observed, StringComparison.Ordinal)) {
				return;
			}
			if (_failed.TryGetValue(alias, out FailedActivation? failed)
					&& string.Equals(identity, failed.Identity, StringComparison.Ordinal)
					&& Environment.TickCount64 < failed.RetryAfter) {
				diagnostics.Add(failed.Diagnostic);
				return;
			}
			bool lockAcquired = _store.TryExecuteWithSourceMutationLock(alias, () => {
				string? lockedRevision = transport.GetCurrentRevision(repositoryPath);
				if (lockedRevision is null) {
					HandleGitFailure(alias, source, identity, diagnostics,
						$"Git knowledge source '{alias}' has no valid current revision.");
					return;
				}
				string lockedIdentity = GitIdentity(source, lockedRevision);
				if (_observed.TryGetValue(alias, out string? lockedObserved)
						&& string.Equals(lockedIdentity, lockedObserved, StringComparison.Ordinal)) {
					return;
				}
				transport.ValidateInstalledCheckout(source, repositoryPath);
				if (!_gitReader.TryRead(repositoryPath, source.LibraryId,
						out KnowledgeGitRepositorySnapshot? snapshot, out string? diagnostic)) {
					HandleGitFailure(alias, source, lockedIdentity, diagnostics,
						diagnostic ?? $"Git knowledge source '{alias}' is invalid.");
					return;
				}
				KnowledgeBundleActivationResult activation = _runtime.ActivateGitRepository(
					alias,
					source.Priority,
					source.Participation,
					snapshot!);
				if (activation.Status != KnowledgeBundleActivationStatus.Activated) {
					HandleGitFailure(alias, source, lockedIdentity, diagnostics,
						activation.Diagnostic ?? $"Git knowledge source '{alias}' was rejected.");
					return;
				}
				_observed[alias] = lockedIdentity;
				_observedGitConfiguration[alias] = GitConfigurationIdentity(source);
				_failed.Remove(alias);
			});
			if (!lockAcquired && !_observed.ContainsKey(alias)) {
				diagnostics.Add($"Git knowledge source '{alias}' is synchronizing; activation will retry on the next request.");
			}
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidDataException
				or InvalidOperationException
				or TimeoutException) {
			HandleGitFailure(alias, source, $"git:{source.LibraryId}:error", diagnostics,
				$"Git knowledge source '{alias}' could not be refreshed: {exception.Message}");
		}
	}

	private static string GitIdentity(KnowledgeSourceConfiguration source, string revision) =>
		$"{GitConfigurationIdentity(source)}:{revision}";

	private static string GitConfigurationIdentity(KnowledgeSourceConfiguration source) =>
		$"git:{source.LibraryId}:{source.Location}:{source.Branch}:{source.Tag}:{source.Commit}:"
		+ $"{source.Priority}:{source.Participation}";

	private void HandleGitFailure(
		string alias,
		KnowledgeSourceConfiguration source,
		string identity,
		ICollection<string> diagnostics,
		string diagnostic) {
		diagnostics.Add(diagnostic);
		RecordFailed(alias, identity, diagnostic);
		if (_observed.ContainsKey(alias)
				&& _observedGitConfiguration.TryGetValue(alias, out string? observedConfiguration)
				&& string.Equals(observedConfiguration, GitConfigurationIdentity(source), StringComparison.Ordinal)) {
			return;
		}
		_runtime.DeactivateLibrary(alias);
		_observed.Remove(alias);
		_observedGitConfiguration.Remove(alias);
	}

	private void RecordFailed(string alias, string identity, string diagnostic) {
		_failed[alias] = new FailedActivation(
			identity,
			diagnostic,
			Environment.TickCount64 + _options.FailureRetryMilliseconds);
	}

	private static bool TopicPinsEqual(
		IReadOnlyDictionary<string, string> left,
		IReadOnlyDictionary<string, string> right) => left.Count == right.Count
		&& left.All(pair => right.TryGetValue(pair.Key, out string? value)
			&& string.Equals(pair.Value, value, StringComparison.Ordinal));

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
			source.TrustedPublicKeyPath!,
			out string fingerprint)
			? fingerprint
			: "unavailable";
		return $"{source.LibraryId}:{pointer.LibraryId}:{pointer.Sequence}:{pointer.BundleDigest}:"
			+ $"{source.Priority}:{source.Participation}:{source.TrustedKeyId}:"
			+ $"{source.TrustedPublicKeyPath}:{trustFingerprint}";
	}

	private sealed record FailedActivation(string Identity, string Diagnostic, long RetryAfter);
}
