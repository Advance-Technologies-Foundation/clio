# Runtime image discovery

Extend the existing operator bootstrap/runtime flow with `runtime images --context <cluster> [--json]`. The runtime feature flag applies. Read the same dashboard catalogue the operator UI uses via the Kubernetes service proxy; no new inventory, registry scan or deployed-instance dependency. Reuse the operator Deployment's dashboard credential values or Secret references when login is required. Kubernetes authorization remains mandatory. Do not print or persist secrets.

One distribution per tag with product, version, runtime, exact Dev/Prod and database references, template readiness and deployability. Table is the default; JSON is an undecorated array, including `[]` when empty. Both formats expose the same information. Incomplete distributions are visible but marked not deployable. No downloads or builds are triggered. This is a host CLI operation; no remote MCP surface is added.

Validation: grouping and incomplete/empty inventory tests; context routing tests for Rancher and Omen; live authenticated catalogue read and table/JSON parity. Dashboard authentication configuration must be a direct environment value or Secret reference. Operator Service/Deployment name is creatio-operator, port 8080; operator namespace defaults to creatio-system and is configurable.
