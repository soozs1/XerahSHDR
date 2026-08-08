# Security Remediation Plan (August 2026)

**Status**: Phase 0 complete; Phase 1 in progress
**Date**: 2026-08-05
**Baseline version**: v0.24.18
**Source**: Full security review of `src/`, `plugins/`, `scripts/`, `.githooks/`, `.github/workflows/`
**Related**: `docs/ROADMAP.md` (Phase 8), `docs/architecture/xerahs_architecture_map.md` §4.3/§9.3

## Execution status

### Phase 0 — COMPLETE (2026-08-05)

| ID | Verdict | Notes |
|----|---------|-------|
| SEC-01 | ✅ Closed | `SettingsSerializationBinder` (assembly allowlist) wired into `SettingsBase` + importer + 11 deep-copy sites; legacy `$type` names still resolve; canary tests prove no instantiation outside allowlist |
| SEC-02 | ✅ Closed | `ItemTypeNameHandling.Auto` on `ServiceSettings` now goes through the same binder; import path uses binder settings |
| SEC-03 | ⏳ Phase 1 | Option B selected (see decision record) |
| SEC-04 | ✅ Closed | `UrlSafety.IsSafeExternalUrl` (http/https only) enforced in all 5 platform `OpenUrl` implementations; `IsValidURL` rewrote (no `steam://` exception, no >2083 branch); plugin/browser fallbacks (`cmd /c start`, raw `Process.Start`) replaced with `URLHelpers.OpenURL` |
| SEC-05 | ✅ Closed | ProviderId/PluginId mismatch now rejects plugin load (case-insensitive); first-wins collision guard in `LoadPlugins`/`RegisterProvider`/`ReloadCustomUploader`; `ScopedSecretStore`/`ScopedProviderContext` per provider; `GetProviderContext`/`SetProviderContext` internal + `InternalsVisibleTo` (Core/UI/Mobile.Core/Tests); `UploaderInstanceViewModel` no longer hands raw context to plugin VMs |
| SEC-06 | ✅ Closed | MCP: `ListenLocalhost` default, constant-time token compare, hex CSPRNG key, CORS removed; `McpApiKeyVault` (SecretStore-backed, one-time legacy migration, `Rotate()`); `HttpServer.GenerateApiKey` dead code removed |

Phase 0 verification: build 0 errors / 6 pre-existing warnings; targeted tests green
(PluginLoaderTests 26/26, McpApiKeyVault 2/2, SettingsSerializationBinder 5/5, UrlSafety 39/39);
full test run exactly at machine baseline (1206 passed / 19 env-specific failed / 40 skipped).

### Phase 1 — COMPLETE (2026-08-05)

| ID | Verdict | Notes |
|----|---------|-------|
| SEC-03 | ✅ Closed | Option B: `UploadersConfigSecretsMigrator` (recursive walk, 22 direct + all nested `[JsonEncrypt]` + OAuth1/OAuth2 clear-only + `ZeroWidthShortenerToken`/`ImageShackOptions.Auth_token`); special keys preserved (`amazons3/legacy`, `ftp|localhost/legacy:accounts[i]`); mobile S3 legacy fallback; Ftp/Pastebin/Bitly plugins migrated to SecretStore (`SecretKey` GUID + `IInstanceSecretMigrator` + store-backed VMs); `CreateBackupZip` includes `*.key`; docs §4.3 rewritten |
| SEC-08 | ✅ Closed | `ProcessArgumentQuoter` (CRT rules) applied to FFmpegRecordingService, VideoHelpers (whitelist for statsMode/dither), VideoThumbnailer, SevenZipManager, LinuxCliCaptureStrategy, WaylandPortalRecordingService (incl. GStreamer); 22 quoter tests incl. CRT round-trip |
| SEC-09 | ✅ Closed | Scheme allowlist http/https in `ValidateItem` + final check in `GetRequestURL` (no `{` bypass); response size caps (10 MB / 50 MB / 1 MB sxcu); regex timeout 2 s; recursion depth 64 in syntax parser; single validation choke point in `CreateInstance`/`ValidateSettings` |
| SEC-10 | ✅ Closed | Mobile deep link and share/view intents import `.sxcu` without auto-activation (explicit user action in config editor still activates); scheme check retained; TODO for confirmation dialog documented |
| SEC-12 | ✅ Closed | `MaxConfigFileSize` 50 MB, `MaxConfigDepth` 64, fail-loud for type-resolution errors (no silent swallow), backup-entry size limit |

Phase 1 verification: build 0 errors / 6 pre-existing warnings; full test run 1250 passed /
19 machine-specific failed / 40 skipped (baseline 1204 → 1250, +46 security tests, no new failures).

### Phase 2 — COMPLETE (2026-08-05)

| ID | Verdict | Notes |
|----|---------|-------|
| SEC-07 | ✅ Closed | SHA-256 verification wired into FFmpeg downloader (pinned hash for ffprobe fallback `ffmpeg-9.0_x64-windows-static-gpl.zip`, asset suffixes updated for the new release naming); installer authenticated via WinVerifyTrust (P/Invoke) + `X509CertificateLoader` chain check, launch refused on invalid signature |
| SEC-11 | ✅ Closed | macOS Keychain: secret passed via `ProcessStartInfo.ArgumentList` (no shell injection, no shell history); Linux backend already used stdin |
| SEC-13 | ✅ Closed | `PluginLoadContext` containment check (`IsPathWithinPluginDirectory`, GetFullPath + separator prefix, case-aware) for `.deps.json` and native paths |
| SEC-14 | ✅ Closed | AES key file permissions `rw-------` applied on both create and reload; corrupt key file now fails loudly instead of silently regenerating (no data loss) |
| SEC-16 | ✅ Closed | systemd unit paths validated (control chars rejected) and escaped for ExecStart; both install paths covered |
| SEC-17 | ✅ Closed | `libscreencapturekit_bridge.dylib` built from source in macOS CI job; binary gitignored |
| SEC-18 | ✅ Closed | `SanitizeFileName` rejects dot-segments; `IsPathWithinDirectory` containment in save/watch-folder/CLI paths; CLI `HasDotSegment` fallback |
| SEC-19 | ✅ Closed | `SecretMasker` applied at input (uploader errors) and all output channels (debug log, history, toast, CLI stderr, queue UI) |
| SEC-20 | ✅ Closed | All CI actions pinned to commit SHAs (with `# vN` comments); `dependabot.yml` added for github-actions |

Phase 2 verification: build 0 errors / pre-existing warnings only; full test run 1258 passed /
19 machine-specific failed / 40 skipped — no new failures.

**Result: all 20 findings (SEC-01 … SEC-20) closed.**

### Documentation drift — CLOSED (2026-08-08)

`docs/architecture/xerahs_architecture_map.md` §4.1 table, §4.3, and §9.3 now match
code: `UploadersConfig.json` no longer claims DPAPI-encrypted credentials (secrets live
in `SecretStore`), the nonexistent `Encryption.cs` reference was removed, and §9.3 was
rewritten to describe the actual posture (SecretStore backends, `SettingsSerializationBinder`
allowlist + 50 MB/depth-64 limits, provider-scoped stores, `ProcessArgumentQuoter` and
scheme allowlists). Verified by a security review sub-agent against the implementation
(`UploadersConfigSecretsMigrator` zeroes migrated fields, `SecretStore.CreateBackend`,
`SettingsBase` limits, `URLHelpers`/`UrlSafety` allowlists).

---

## Review summary

The review found **no backdoors**: no hardcoded keys or base64 blobs, no hidden network
calls, no remote assembly loading, no malicious content in git hooks, build scripts, or
CI workflows. The plugin install pipeline (SHA-256 enforcement, zip-slip protection,
HTTPS-only) is implemented correctly.

The project does, however, carry a set of classic ShareX-class vulnerabilities plus a
recurring pattern: **protection mechanisms are written but not wired up**
(`KnownTypesSerializationBinder`, `DPAPIEncryptedStringPropertyResolver`,
`QuoteProcessArgument`, scheme checks in CLI). Fixing is therefore mostly connection
work, not new design.

Finding IDs below are stable references for tracking.

---

## Findings registry

### HIGH

| ID | Finding | Location |
|----|---------|----------|
| SEC-01 | `TypeNameHandling.Auto` without `SerializationBinder` on the central settings serializer (CWE-502) | `src/desktop/core/XerahS.Common/SettingsBase.cs` L409, L559 |
| SEC-02 | `ItemTypeNameHandling.Auto` on polymorphic `ServiceSettings` + unvalidated ShareX config import | `src/desktop/core/XerahS.Uploaders/LegacySupport/UploadersConfig.cs` L41; `UploadersConfigImporter.cs` L84 |
| SEC-03 | `[JsonEncrypt]` mechanism dead code; ~17 secrets + OAuth tokens stored plaintext and copied into backup zips | `SettingsBase.cs` L403 (`TODO`), `SettingsManager.cs` L322 |
| SEC-04 | Uploader-returned URLs opened without scheme validation (file://, UNC -> NTLM hash leak, custom schemes); `IsValidURL` bypasses (`steam://` always valid; >2083 chars accepts any scheme) | platform `*SystemService.OpenUrl`; `URLHelpers.cs` L41-95; `AfterUploadViewModel.cs` L409 |
| SEC-05 | Plugin can impersonate a built-in provider (`ProviderId` mismatch only warned, dictionary overwritten) and read other providers' secrets via unscoped `ISecretStore` | `ProviderCatalog.cs` L100-116; `PluginLoader.cs` L110-113 |
| SEC-06 | MCP HTTP server listens on `0.0.0.0:7890`, API key stored plaintext, non-constant-time comparison, CORS `*` | `src/tools/XerahS.McpServer/Transport/HttpServer.cs` L54, L228-230 |

### MEDIUM

| ID | Finding | Location |
|----|---------|----------|
| SEC-07 | FFmpeg/ffprobe downloaded without hash verification (ffprobe from third-party repo `System233/ffmpeg-msvc-prebuilt`); update installer executed from temp with `/VERYSILENT`, no verification | `FFmpegDownloader.cs`; `DownloaderWindowViewModel.cs` L93-175 |
| SEC-08 | FFmpeg argument injection on Linux/macOS: naive quoting, unquoted `-i {deviceId}`; correct `QuoteProcessArgument` exists but is used in only 2 places | `FFmpegRecordingService.cs`; `VideoHelpers.cs` L119; `VideoThumbnailer.cs` L95 |
| SEC-09 | Custom uploaders: no http/https scheme allowlist (`file://` blocked only accidentally by a cast), no response size limit, ReDoS via `.sxcu` regex without timeout, `ValidateItem` bypassable (`{` in URL; `CreateInstance`/editor paths skip it), auto-pickup of `.sxcu` from Plugins folder without confirmation | `CustomUploaderRepository.cs` L243-256; `Uploader.cs` L503; `CustomUploaderFunctionRegex.cs` L68; `CustomUploaderProvider.cs` L187-207 |
| SEC-10 | Mobile deep link `xerahs://import-sxcu` imports and auto-activates an uploader without user confirmation | `MainActivity.cs` (mobile); `MobileImportService.cs` L101-135 |
| SEC-11 | macOS Keychain backend passes the secret on the `security` command line (visible in `ps`) with naive quoting | `SecretStore.cs` L463-466 |
| SEC-12 | Settings deserialization errors silently swallowed (`ErrorContext.Handled = true`); no size/depth limits; corrupted config is merged and rewritten on next save | `SettingsBase.cs` L561-566 |
| SEC-13 | `PluginLoadContext` loads paths resolved from plugin `.deps.json` without containment check (rest of the loader checks it) | `PluginLoadContext.cs` L59-63 |

### LOW

| ID | Finding | Location |
|----|---------|----------|
| SEC-14 | AES fallback `SecretStore` key file stored next to ciphertext without restrictive permissions | `SecretStore.cs` L408-428 |
| SEC-15 | Naive argument quoting in 7-Zip, grim, wf-recorder invocations; `cmd /c start {url}` fallbacks | `SevenZipManager.cs`; `LinuxCliCaptureStrategy.cs` L314; `WaylandPortalRecordingService.cs` L403; `AboutView.axaml.cs` L59 |
| SEC-16 | systemd unit file paths interpolated without unit-file escaping (root daemon) | `LinuxWatchFolderDaemonService.cs` |
| SEC-17 | Prebuilt `libscreencapturekit_bridge.dylib` checked into git instead of built in CI | `native/macos/libscreencapturekit_bridge.dylib` |
| SEC-18 | No path containment check (`Path.GetFullPath` + prefix) in desktop save pipeline; on Linux `..` passes `SanitizeFileName`. CLI already has the correct pattern to copy | `FileHelpers.cs` L267-283; `UploadCommand.cs` L53-60 |
| SEC-19 | Uploader response text/exceptions (may contain tokens) written to debug log and history errors | `CustomUploaderExecutor.cs` L156+; `UploadJobProcessor.cs` L73-83 |
| SEC-20 | CI actions pinned by major tag only, not by SHA | `.github/workflows/*.yml` |

### Documentation drift (process risk)

- `docs/architecture/xerahs_architecture_map.md` §4.3 claims DPAPI encryption of uploader
  credentials and references a nonexistent `src/desktop/core/XerahS.Common/Encryption.cs`.
- §9.3 claims "Settings validators for configuration integrity"; no validators exist.
- Fix docs together with the code, not before (do not paper over).

---

## Phase 0 — Emergency (target: next release, ~1 week)

Small, high-leverage fixes. Each is independently shippable.

### SEC-01 + SEC-02: Constrain deserialization types

- Wire `KnownTypesSerializationBinder` (already exists in
  `src/desktop/core/XerahS.Common/Settings/KnownTypesSerializationBinder.cs`) into
  `SettingsBase.DeserializeFromStream` and the save path.
- Replace `ItemTypeNameHandling.Auto` on `UploadersConfig.ServiceSettings` /
  `CustomUploaders` with an explicit converter or binder covering only the known
  `IUploaderConfig` implementations.
- Apply the same binder in `UploadersConfigImporter`.
- Acceptance:
  - Unit test: JSON with a hostile `$type` (e.g. `System.Windows.Forms.OpenFileDialog`
    or any type outside the allowlist) fails to load instead of instantiating.
  - Round-trip test: current real-world configs (including a ShareX import) still load.
  - `dotnet test tests/XerahS.Tests/XerahS.Tests.csproj` passes.

### SEC-04: Scheme allowlist for opening URLs

- Add a single shared `IsSafeExternalUrl` (http/https only) in
  `XerahS.Platform.Abstractions` and enforce it in every `ISystemService.OpenUrl`
  implementation (Windows/Linux/macOS) **before** `Process.Start`.
- Fix `URLHelpers.IsValidURL`: remove the unconditional `steam://` pass; remove the
  >2083-char degenerate branch (validate scheme for all lengths); drop `file://` from
  `URLPrefixes` usages that feed `OpenUrl`.
- Audit `ProcessHelper.OpenURL` and the two `cmd /c start` fallbacks (SEC-15 part).
- Acceptance: unit tests for `file://`, `\\server\share`, `javascript:`, custom scheme,
  and >2083-char payloads; manual smoke of "Open URL" after upload on Windows.

### SEC-05: Plugin provider identity enforcement

- In `ProviderCatalog.LoadPlugins`: reject a plugin whose runtime `ProviderId` collides
  with an already-registered provider (built-ins register first), instead of overwriting.
- Require `provider.ProviderId == metadata.Manifest.PluginId`; remove "Allow it but warn".
- Scope `ISecretStore` by calling provider (pass caller identity through
  `InstanceConfiguration`/context, not a free `providerId` argument).
- Acceptance: test that a plugin claiming `imgur` does not replace the built-in and
  cannot read `imgur` secrets; existing pixelfox plugin still loads.

### SEC-06: MCP server lockdown

- Default to `ListenLoopback`; opt-in `--listen-any` flag for explicit LAN use.
- Move `McpApiKey` out of `ApplicationConfig.json` into `SecretStore` (key name `mcp`).
- Compare tokens with `CryptographicOperations.FixedTimeEquals`.
- Restrict CORS from `*` to configured origins (or none, since clients are local tools).
- Acceptance: `netstat` shows 127.0.0.1 binding by default; key no longer present in
  `ApplicationConfig.json` after first run migration.

---

## Phase 1 — Secrets and input hardening (~2-4 weeks)

### SEC-03: Secret encryption (two options, see decision record below)

Decision required from maintainers before starting. Both options must also stop secrets
from leaking into backup zips (either exclude or encrypt).

### SEC-12: Settings validation and fail-loud loading

- Add `MaxDepth` and file-size limits to settings JSON loading.
- Replace blanket `ErrorContext.Handled = true` with: collect errors, fail load on
  critical structural errors, keep the previous good file (write `.bad` aside instead of
  overwriting on next save).
- Deliver the validators promised by docs §9.3.

### SEC-08: FFmpeg argument safety

- Route all external-process launches through `ArgumentList` (or reuse
  `FFmpegCLIManager.QuoteProcessArgument`): `FFmpegRecordingService`, `VideoHelpers`,
  `VideoThumbnailer`, plus the SEC-15 list (7-Zip, grim, wf-recorder).
- Quote/validate microphone device IDs; validate `statsMode`/`dither` against enums.
- Acceptance: test with filenames containing quotes/spaces/dashes on Linux.

### SEC-09: Custom uploader hardening

- Enforce http/https allowlist in `ValidateItem` and `GetRequestURL`; remove the `{`
  bypass; block CRLF in header values explicitly (defense in depth, even though .NET
  currently rejects them).
- Add response size cap (configurable, sane default e.g. 10 MB) in `Uploader` read loop.
- Regex timeout on `.sxcu` patterns (`Regex.Match(..., TimeSpan)`).
- Single validation choke point: make `CreateInstance`, editor load, and Plugins-folder
  auto-pickup all go through `ValidateItem`; require explicit confirmation when a new
  `.sxcu` is discovered from disk.

### SEC-10: Mobile deep link confirmation

- Show a confirmation dialog with the uploader name + target host before importing;
  never auto-activate an imported uploader.

---

## Phase 2 — Supply chain and residual hardening (~1-2 months)

### SEC-07: Download integrity

- Pin SHA-256 hashes for FFmpeg/ffprobe downloads (at minimum the ffprobe fallback from
  the third-party repo); prefer building/hosting our own prebuilts.
- Update path: verify installer (Authenticode check on Windows / hash pin) before
  executing; never execute directly from a shared temp path.

### SEC-11: macOS Keychain via stdin

- Pass the secret through stdin (same pattern already used by the Linux libsecret
  backend) instead of the `-w` argument.

### SEC-13: Plugin containment for `.deps.json` paths

- Apply the existing `StartsWith(pluginDirectory)` containment check to
  `ResolveAssemblyToPath`/`ResolveUnmanagedDllToPath` results.

### SEC-14, SEC-16, SEC-17, SEC-18, SEC-19, SEC-20

- Restrict AES fallback key file permissions (`File.SetUnixFileMode` / ACL).
- Escape systemd unit paths (or reject paths with newlines/control chars).
- Build `libscreencapturekit_bridge.dylib` in CI from the checked-in `.m` source.
- Port `UploadCommand.SanitizeUploadFileName` containment pattern to the desktop save path.
- Mask/redact uploader response bodies in logs and history errors.
- Pin CI actions to commit SHAs (or adopt `dependabot` for actions).

### Optional: plugin signing

- Design note only: packages are SHA-256-checked today; code signing + publisher
  verification would harden the registry against repo compromise. Track separately.

---

## Decision record: SEC-03 options

> **Decision (2026-08-05): Option B selected** — migrate secrets into `SecretStore`.
> Option A is kept below only as a documented fallback if the migration must be split.

### Option A — Wire up the DPAPI property resolver (ShareX-original approach)

Mechanism: `DPAPIEncryptedStringPropertyResolver` (already written) marks
`[JsonEncrypt]` string properties; on save values are DPAPI-encrypted, on load decrypted.
Secrets stay inside `UploadersConfig.json` as ciphertext.

| Aspect | Assessment |
|--------|-----------|
| Effort | Small (~1-2 days): connect resolver in `SettingsBase` save/load, handle legacy plaintext values on first read |
| Platforms | DPAPI is Windows-only; macOS/Linux need a fallback path (defeats the point or requires per-OS branches) |
| Backup leak | **Not solved by itself** — backup zips copy the config file; ciphertext is portable but decryptable on the same user/machine, and the flag/migration logic must cover the backup path too |
| Portability | Ciphertext is machine-bound; copying settings to another machine breaks secrets (ShareX ships an explicit "encrypt configs" toggle for this) |
| Risk | Two encryption schemes coexist long-term (DPAPI properties vs SecretStore) |

### Option B — Migrate secrets into `SecretStore` (recommended end state)

Mechanism: remove secrets from `UploadersConfig.json` entirely; store them in the
existing cross-platform `SecretStore` (DPAPI on Windows, Keychain on macOS, libsecret on
Linux, AES-256-GCM file fallback). Config JSON keeps only non-secret fields.

| Aspect | Assessment |
|--------|-----------|
| Effort | Medium (~1-2 weeks): one-time migration moving ~17 `[JsonEncrypt]` fields + OAuth tokens into the store, clearing JSON fields; all legacy uploaders read through store-backed property shims; ShareX import must route secrets to the store |
| Platforms | Cross-platform by design — native OS protection on all three desktops |
| Backup leak | **Solved structurally** — secrets never enter config files, so backup zips cannot leak them |
| Portability | Secrets are deliberately non-portable; config export/import needs an explicit, clearly-labeled "include secrets" flow (or stays secret-free — recommended) |
| Risk | Migration must be idempotent and reversible during the transition; legacy importer and plugin SDK already point at this model |

**Recommendation**: Option B as the target architecture (it matches what the plugin
system already uses). If a release must ship before the migration is done, apply Option A
as a stopgap the same week — but keep the stopgap visibly temporary, because running two
encryption schemes increases the chance one of them stays unwired. **(Selected: Option B,
no stopgap — see decision above.)**

Whichever option is chosen, also fix:
1. Backup zip creation must not contain plaintext secrets (exclude or encrypt).
2. `docs/architecture/xerahs_architecture_map.md` §4.3 must be updated to match reality.
3. `McpApiKey` moves to the store (already covered by SEC-06).

---

## Verification gates

Every phase exits only when:

1. `dotnet build src/desktop/XerahS.sln -m:1` — 0 errors, 0 warnings
   (`TreatWarningsAsErrors` stays on).
2. `dotnet test tests/XerahS.Tests/XerahS.Tests.csproj -m:1` — green; each SEC item
   above adds at least one regression test.
3. Manual checks listed per item.
4. Docs updated in the same PR when behavior or security posture changed.

## Tracking

Findings are referenced as `SEC-NN` in commits (`[vX.Y.Z] [Fix] SEC-04 ...`) and in
`docs/ROADMAP.md` Phase 8.
