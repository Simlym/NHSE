# NHSE desktop workspace

The Windows entry point now hosts a locally bundled Vue application in WebView2.
The welcome page, player balances, pockets and home storage use this workspace.
Villagers, maps, designs, detailed item properties and console tools continue to
use their existing C# editors, reached through the workspace. No server is needed.

## Build and run

Requires the .NET 10 SDK, Node.js 22.12+ (Node 24 also works), and npm.

```powershell
dotnet build NHSE.WinForms/NHSE.WinForms.csproj -c Release
```

The project runs `npm ci` and `npm run build`, then copies the bundled page into
`Workspace/` next to the executable. Publish also includes these assets. Users
need the Microsoft Edge WebView2 Runtime, but do not need Node.js or npm.
If the runtime or assets are unavailable, a localized recovery screen offers the
original editor. Ship the entire publish directory, including native loaders.

For UI-only development:

```powershell
cd NHSE.UI
npm ci
npm run dev
```

An ordinary browser shows the welcome page and a preview notice. Real file access
is available only inside the Windows host. Development does not silently load
sample saves or upload user data. To reuse already built frontend assets during
C# iteration, pass `-p:SkipWorkspaceBuild=true`.

## Editing model

`WorkspaceSession` owns the existing `HorizonSave` model and an in-memory undo /
redo history. Applying a field or item changes memory only. Save calls the existing
hashing, encryption and provider-flush code. Opening a save retains the existing
validation and automatic-backup settings. Close, replace and exit prompt when
applied changes have not been saved. Numeric drafts require Apply.

Pocket slots preserve the game's existing pocket/bag order. Locked slots cannot
be edited. Stack quantities are presented starting at one and converted to the
game's zero-based count only for stackable items; non-stack item parameters are
preserved. Detailed native editing remains available for recipes, flowers,
variants, wrapping and other specialized properties.

The original editor shares the current in-memory save. Its fields are applied
when it closes. Returning conservatively marks the session dirty and clears the
workspace's undo history because legacy editors cannot provide reversible
commands. The interface explains this boundary. The original editor retains
its own explicit save command and existing behavior.

## Localization

`src/locales/*.json` contains all UI and native message keys for the nine existing
language codes: en, jp, de, es, fr, it, ko, zhs, zht. JSON files are also embedded
in the C# host, so startup and unsaved-change dialogs use the selected language.
Language changes update `GameInfo`, legacy messages, item names and persisted
settings. The English resource is the defensive fallback. Add new keys to every
locale; `npm test` checks key parity and nonempty text.

## Bridge

The page is mapped to `https://nhse.local/` from local assets. Navigation away
from this origin, new windows and permissions are blocked. Host objects are
disabled. The page sends bounded, named commands; it cannot supply arbitrary
filesystem paths or executable code. Open uses native file/folder dialogs. Drop
uses WebView2's file additional objects. Request IDs correlate replies; concurrent
native operations are rejected while a dialog or edit is active.

## Checks

```powershell
cd NHSE.UI
npm test
npm run build
npx playwright install chromium
npm run test:ui
cd ..
dotnet run --project NHSE.Workspace.Tests -c Release
```

The C# integration harness generates encrypted synthetic files entirely in memory
and checks pocket order, raw parameter preservation, locked slots, undo branches,
save/reload and hashes. Browser tests use an explicit test-only host and exercise
all nine languages, compact widths, quantity edits and validation, dialogs, focus
mode and saved state.

For an installed Windows WebView2 runtime, the native smoke script also checks the
actual C# bridge, language switching, paging, full screen and origin restriction:

```powershell
dotnet run --project NHSE.Workspace.Tests -c Release -- --fixture artifacts/workspace-fixture
pwsh -NoProfile -STA -File NHSE.Tests/Workspace/Smoke-Workspace.ps1 `
  -BuildDirectory NHSE.WinForms/bin/Release/net10.0-windows `
  -FixtureDirectory artifacts/workspace-fixture `
  -OutputDirectory artifacts/workspace-native
```

Only the generated fixture is used. No actual console or user save is required.
