# Contract: GitHub REST interactions

Consumed external interface. Base URL `https://api.github.com` (fixed; HTTPS only).
Every request sends:

```http
Authorization: Bearer <token>
Accept: application/vnd.github+json
X-GitHub-Api-Version: 2022-11-28
User-Agent: KeeDroidSign/<assembly version>
```

The fake `HttpMessageHandler` in tests MUST reproduce these responses.

## ValidateTokenAsync

`GET /user`

| Response | Result |
|----------|--------|
| 200 `{ "login": "octocat" }` | `Valid`, Identity.Login = `octocat`, scopes from `X-OAuth-Scopes` |
| 401 | `InvalidOrExpired` |
| 403 with `x-ratelimit-remaining: 0`, or 429 | `RateLimited` |
| 403 otherwise | `InsufficientPermissions` |
| other status (e.g. 5xx), network failure, timeout | `NetworkError` |

## CheckRepositoryAccessAsync

1. `GET /repos/{owner}/{repo}` — 404 → `NotFoundOrNoAccess`; 200 with `"archived": true` →
   `Archived`.
2. `GET /repos/{owner}/{repo}/actions/secrets?per_page=1` — 200 → `CanManageSecrets`;
   403/404 → `InsufficientPermissions`.
3. Either call: 429 or 403 with `x-ratelimit-remaining: 0` → `RateLimited`; network failure →
   `NetworkError`.

## ListSecretNamesAsync

`GET /repos/{owner}/{repo}/actions/secrets?per_page=100&page={n}` starting at `n=1`.
Response `{ "total_count": N, "secrets": [ { "name": "..." }, ... ] }`; continue while collected
< `total_count` and the page is non-empty. Names compared case-insensitively.

## GetPublicKeyAsync

`GET /repos/{owner}/{repo}/actions/secrets/public-key` →
`{ "key_id": "012345678912345678", "key": "<base64 32-byte X25519 public key>" }`.
A decoded key whose length ≠ 32 is an error.

## PutSecretAsync

`PUT /repos/{owner}/{repo}/actions/secrets/{secret_name}`

```json
{ "encrypted_value": "<base64(crypto_box_seal(utf8(value), key))>", "key_id": "<key_id>" }
```

| Response | SecretWriteStatus |
|----------|-------------------|
| 201 | `Created` |
| 204 | `Updated` |
| 403 / 404 | `Failed` — "insufficient permissions" |
| 422 | `Failed` — "rejected by GitHub (validation)" |
| 403 rate limit / 429 | `Failed` — "rate limited" |
| other / network | `Failed` — status code or "network error" |

Response bodies are never copied verbatim into `Reason` (they may echo input).

## Secret values (default mapping)

| Secret | Value |
|--------|-------|
| `ANDROID_KEYSTORE_BASE64` | single-line Base64 of the JKS bytes |
| `ANDROID_KEYSTORE_PASSWORD` | store password |
| `ANDROID_KEY_ALIAS` | alias |
| `ANDROID_KEY_PASSWORD` | key password |

These match the reference workflow in the spec's Clarifications section.
