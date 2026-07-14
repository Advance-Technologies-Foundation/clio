# Explorer deploy local defaults QA

## Automated coverage

- Resolver selects one enabled local database server.
- Resolver preserves omitted database selection outside Explorer as Kubernetes intent.
- Resolver preserves explicit and deploy-specific database names.
- Resolver leaves `DbServerName` empty for zero or multiple enabled local servers.
- Registry artifact uses conditional failure pause for both ZIP deploy registrations without outer payload quoting.

## Manual validation

- Import the registry integration through `clio register`.
- Deploy the supplied Creatio ZIP with local PostgreSQL and Redis available and Kubernetes unavailable.
- Confirm local restore selection and confirm a forced failing invocation leaves the error terminal open.
