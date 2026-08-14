# Testing & Verification

- **Requirement**: Suggest relevant tests if modifying executable code.
- **Conventions**: Align new tests with current conventions.
- **Historical Parity**:
  - Use `git show <hash>:<file>` to compare against historical ShareX behavior if needed.
  - Store references in `ref/` and clean up after validation.

## Target Platform (Windows only)

Testing and local verification target **Windows** only, with a single RID so the
native-asset copy stays bounded.

**Do not pass `-r` to the solution.** `dotnet build src/desktop/XerahS.sln -r win-x64`
fails with `NETSDK1134` (the .NET SDK does not support building a *solution* with a
RuntimeIdentifier). Build the individual project instead.

### Build (Windows app + bundled plugins)

Windows (native):

```
dotnet build src/desktop/app/XerahS.App/XerahS.App.csproj -r win-x64
```

Linux/macOS/WSL (cross-compile the Windows app):

```
dotnet build src/desktop/app/XerahS.App/XerahS.App.csproj -r win-x64 -p:EnableWindowsTargeting=true
```

### Test

Windows (native, runs the suite):

```
dotnet test tests/XerahS.Tests/XerahS.Tests.csproj -r win-x64
```

Linux/macOS/WSL: a `win-x64` test host cannot execute here, so cross-compiling only
validates the build. To actually run the suite on a non-Windows host, use the host RID:

```
dotnet test tests/XerahS.Tests/XerahS.Tests.csproj -r linux-x64
```

### Environment (suppress the SDK telemetry/first-run banner)

Set these so the .NET SDK banner doesn't appear in every build:

PowerShell (Windows):

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
```

bash/sh (Linux/macOS/WSL):

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
```

### Why a single RID (the disk-space fix)

SkiaSharp ships native binaries for ~16 RIDs (linux/linux-musl/linux-bionic/win/osx
across x86/x64/arm64/riscv64/loongarch64). A portable (no `-r`) build copies all of
them into every executable/plugin `bin/`, ballooning the working tree (measured ~8 GB
of `bin`/`obj`). Passing `-r win-x64` prunes this to a single RID (a plugin dropped
from ~595 MB to ~152 MB).

### Why `-r` on a project, not the solution or a config default

- The solution rejects `-r` with `NETSDK1134`, so the RID must be scoped to a project.
- A regular-property `RuntimeIdentifier` default in `Directory.Build.props` does not
  propagate as a global property to the isolated project references (`XerahS.UI`,
  `ShareX.ImageEditor`, `ShareX.VideoEditor`), so their TFMs fall out of sync
  (`NU1201`/`NETSDK1047`). `Directory.Build.rsp` is not applied by `dotnet build`'s
  implicit restore.
- `-r` on an individual project is a true global property (propagates to every project
  reference) and is the mechanism the multi-platform release scripts already use.

## Mobile Theming Validation

- Run targeted mobile builds for UI and head projects when changing mobile styles:
  - `dotnet build src/XerahS.Mobile.UI/XerahS.Mobile.UI.csproj`
  - `dotnet build src/XerahS.Mobile.iOS.ShareExtension/XerahS.Mobile.iOS.ShareExtension.csproj`
  - `dotnet build src/XerahS.Mobile.iOS/XerahS.Mobile.iOS.csproj`
  - `dotnet build src/XerahS.Mobile.Android/XerahS.Mobile.Android.csproj`
- Capture manual simulator/device results in `docs/reports/`.
