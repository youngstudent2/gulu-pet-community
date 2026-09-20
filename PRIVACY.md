# Privacy

Gulu Pet Community is offline by default. The stock application does not send telemetry, upload logs, call a remote diary service, check for updates, or query weather and foreground-application context.

Settings, care state, relationship state, diary entries, and diagnostics are stored locally under `%LOCALAPPDATA%\GuluPetCommunity`. The uninstaller keeps this data unless the user explicitly chooses to remove it. Local diagnostic files may contain application state and error details; review them before sharing.

The source tree includes generic integration classes for optional HTTP diary generation, error-report submission, updates, weather, and runtime context. They are disabled or unconfigured in the stock composition. A fork that enables any integration is responsible for documenting its endpoint, data collection, retention, consent, and deletion behavior.

No credentials are bundled with the repository. Do not commit API keys, access tokens, certificates, personal data, or generated `.env` files.
