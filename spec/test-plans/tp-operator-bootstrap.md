# Operator bootstrap validation

- TC-U-01: unsupported targets and empty/invalid options fail before cluster mutations.
- TC-U-02: every kubectl call includes the selected context; no use-context, deletion or Nexus deployment.
- TC-U-03: existing secret is preserved; initial secret is created without logging credentials.
- TC-U-04: shared policy is created before the operator; existing Nexus-managed policy is rejected.
- TC-I-01: generated bundle contains CRDs, pinned manager image, ingress and no daemonsets/Nexus installation resources.
- TC-I-02: install and repeat install on Rancher Moby; verify dashboard, deployment readiness and no Nexus workload, and unchanged existing infrastructure identities.
- TC-I-03: build/load an attachable image locally, provision through the operator and verify two-way attachment. Verified with a .NET 10 archive, Ready runtime and file edits originating on both endpoints.

- TC-U-05: build orchestration validates runtime metadata, uses the Rancher image store and removes only its staging resources on success or failure.
- TC-U-06: runtime creation rejects unsafe names and image references and explicitly requests template database restoration.
- Validation boundary: a running debugger process is not a verified breakpoint; source activation and breakpoint tests are not claimed by this bootstrap slice.

## Live acceptance results

Validated with Rancher Desktop Moby and a Creatio 10.2.231 .NET 10 archive:

- Installation completed; dashboard returned HTTP 200.
- Repeat installation preserved the dashboard Secret UID and shared-infrastructure UID.
- No Nexus workload was installed. No cluster reset was necessary.
- Local base/database/Dev image build completed; temporary build volumes were cleaned.
- Runtime database restoration completed and the instance reached Ready.
- Host-to-runtime and runtime-to-host package file changes were observed through the attachment.
- The source build targets .NET 8 and .NET 10; test project targets .NET 10.

The bootstrap uses the existing operator resources, Docker image store and attachment implementation. It adds no registry, separate environment inventory or custom synchronization protocol. Manual FSM and SSH preparation remain the main usability gaps.

Automated result: 14,102 passed, 25 skipped, zero failures with `dotnet test clio.tests/clio.tests.csproj -f net10.0 --no-build --no-restore --filter 'Category=Unit|FullyQualifiedName~OperatorBundleTests|FullyQualifiedName~LocalRuntimeImageBuilderTests'`. `dotnet build clio/clio.csproj -f net8.0 --no-restore` succeeded with existing warnings. Parallel correctness, intent and simplicity reviews completed; reported ownership and runtime-name findings were addressed and rechecked.
