# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| latest release | ✅ |
| older releases | ❌ (update via `scdms --update`) |

## Reporting a Vulnerability

Please report security vulnerabilities via [GitHub Security Advisories](https://github.com/MPCoreDeveloper/SCDMS/security/advisories/new) on this repository. Do **not** open a public issue.

We aim to acknowledge reports within 7 days.

## Security posture of SCDMS

- Binds to `localhost` by default; HTTPS-only endpoint.
- TLS uses a locally generated, self-signed `localhost` certificate stored per-user (no .NET SDK dependency). Accept the one-time browser warning or trust it via your OS certificate store.
- SafeWebCore strict A+ security headers, CSP nonces, HttpOnly+Secure+SameSite session cookies.
- Recent-connection profiles never persist passwords.
- Installers verify SHA256 checksums of release assets.
- Update checks call the GitHub Releases API at most once per 24 hours and can be disabled (`SCDMS__UpdateCheckEnabled=false`).
