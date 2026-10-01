using System;

namespace Clio.Command;

/// <summary>All built-in experimental features. Persisted keys are defined by FeatureNames.</summary>
public enum ExperimentalFeature {
	/// <summary>Identity deployment and OAuth administration.</summary>
	DeployIdentity,
	/// <summary>Freedom UI mobile page conversion.</summary>
	MobilePageConverter,
	/// <summary>ClioRing desktop companion.</summary>
	Ring,
	/// <summary>Operator bootstrap and runtime lifecycle and attachment.</summary>
	Runtime,
	/// <summary>Compilation watching.</summary>
	WatchCompilation,
	/// <summary>Local development support for unsequenced knowledge bundles.</summary>
	KnowledgeAllowUnsequenced
}

/// <summary>Stable external names for feature flags; keeps existing settings and CLI names compatible.</summary>
public static class FeatureNames {
	/// <summary>Returns the persisted, kebab-case key for a built-in feature.</summary>
	public static string ToKey(this ExperimentalFeature feature) => feature switch {
		ExperimentalFeature.DeployIdentity => "deploy-identity",
		ExperimentalFeature.MobilePageConverter => "mobile-page-converter",
		ExperimentalFeature.Ring => "ring",
		ExperimentalFeature.Runtime => "runtime",
		ExperimentalFeature.WatchCompilation => "watch-compilation",
		ExperimentalFeature.KnowledgeAllowUnsequenced => "knowledge-allow-unsequenced",
		_ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown feature.")
	};

	/// <summary>Checks a built-in feature through the existing live or frozen feature service.</summary>
	public static bool IsFeatureEnabled(this IFeatureToggleService service, ExperimentalFeature feature) => service.IsFeatureEnabled(feature.ToKey());
}
