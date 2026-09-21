# Verify a local file

`verify-file` compares a file's SHA-256 against a required expected digest. It reads the file with bounded memory and does not change it. This is an additional new operation; the legacy migration inventory remains 21 ports.

```powershell
clio verify-file --path ./download.zip --sha256 <64-hex-character-digest>
```

Both `path` and `sha256` are required strings. The digest accepts uppercase or lowercase hexadecimal, with exactly 64 characters and no surrounding whitespace. Relative paths resolve from the process working directory. No Creatio connection is required.

The result payload contains `sha256` (actual lowercase digest), `bytes` (actual number of bytes read) and `matches`. A match returns `Accepted=true`, `Code=completed`, exit 0. A mismatch retains this payload but returns `Accepted=false`, `Code=hash-mismatch`, exit 1. Missing files return `file-not-found`, access denied returns `file-access-denied`, other read I/O failures return `file-read-failed`. Malformed input returns `invalid-arguments`; omitted required CLI flags keep the generic parser's exit 2/stderr behavior. Cancellation follows the existing adapter cancellation contract.

MCP uses the existing `list-operations` and `execute` tools. Call `execute` with:

```json
{"operation":"verify-file","arguments":{"path":"./abc.txt","sha256":"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"}}
```

For a file containing exactly the three UTF-8 bytes `abc`, the payload is:

```json
{"sha256":"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","bytes":3,"matches":true}
```

The workflow validates and compares. The runtime-owned `file-hash` primitive performs streaming I/O. Core, resident host contracts and adapters need no feature code. See the [pilot record](agent-runs/verify-file.md) for complete-runtime 10.10.0 packaging and unchanged preview20 host verification.

This verifies bytes observed during the read; it does not establish who published a file or guarantee that another process cannot change it later.
