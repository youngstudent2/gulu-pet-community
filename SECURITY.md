# Security Policy

## Supported versions

Security fixes are provided for the latest released version of Gulu Pet Community.

## Reporting a vulnerability

Please report vulnerabilities privately through the repository's GitHub Security tab by opening a private security advisory. Do not disclose a suspected vulnerability in a public issue before a fix is available.

Include the affected version, reproduction steps, impact, and any suggested mitigation. Remove credentials, tokens, personal data, and unrelated local files from logs or screenshots before attaching them.

## Security boundaries

The stock build is offline by default. Fork maintainers who enable optional network integrations must provide their own authenticated endpoints, transport-security policy, privacy notice, and secret-management process. Secrets must not be embedded in source code, assets, installer scripts, or release archives.
