# NHSE desktop workspace

The Windows entry point now hosts a locally bundled Vue application in WebView2.
The welcome page, player names, island names, balances, capacity, pockets and home
storage, resident profiles and island settings use this workspace. Players switch through portrait/name cards using the
photos stored in the save, with a generic avatar when no photo is available.
Resident personality, catchphrase, default phrase, moving-out status and template
replacement are editable in the workspace. Airport colors, both hemispheres and
unsigned weather seeds are also editable here. Resident/house replacement is one
undoable operation. No server is needed.

Turnip prices now use a native Vue weekly AM/PM table, including purchase price,
pattern and the raw fever-start value. Valid drafts apply on save or navigation.
The shared 40-slot recycle box uses the workspace inventory grid and item catalog;
it is accessible from both inventory and the island shortcut. Both editors join
the same undo/redo history and only write to disk on Save.

Terrain, houses, campsite, fruit/flowers and all four design
editors open directly from the workspace's labeled shortcuts. These specialized
C# editors share the current save; changed byte ranges across main and player
files join the workspace's undo history. Unchanged dialogs add no history; an
exception restores the pre-dialog bytes. Detailed properties, import/export and
console tools remain available in the full original editor.

The workspace and original villager editor's Replace button/portrait open the Vue portrait
gallery. The current-resident strip can switch the resident being edited; the
gallery filters by the game's species and template personality, searches localized
names or IDs, paginates, and previews a replacement before confirmation. Already
resident villagers are marked to avoid duplicate replacements. Clipboard-based
replacement remains on the original button's context menu. Replacement preparation
uses independent copies and retains house assignment fields for both house formats.

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
applied changes have not been saved. Profile drafts can be applied explicitly;
saving, leaving the profile page or switching players also applies valid drafts.
Names are validated against the game's 10 UTF-16-code-unit limit. Renames update
matching identity references across the save, using exact offset patches so undo
does not accidentally rename pre-existing identities with the same new name.
The profile also exposes pocket/storage capacity and, where available, hotel
tickets and Poki. Unchanged pocket layouts are preserved.

Pocket slots preserve the game's existing pocket/bag order. Locked slots cannot
be edited. Stack quantities are presented starting at one and converted to the
game's zero-based count only for stackable items; non-stack item parameters are
preserved. Detailed native editing remains available for recipes, flowers,
variants, wrapping and other specialized properties.

The item catalog covers the same item IDs as the original inventory selector,
excluding the empty/extension markers (empty slots use Remove). It is paginated
at 96 items per page rather than truncating the results. Categories are grouped
from the existing game `ItemKind` metadata, with an Other group so unfamiliar
types remain reachable. It supports localized-name/decimal-ID search, category
counts, first/last page navigation, direct page jumps and large picture/name
tiles. Catalog navigation remains local to the editor and can reach every item.
Clothing has a second filter level using the existing `ItemKind` values: tops,
bottoms, dresses/outfits, headwear, accessories, socks, shoes, bags, umbrellas and
an Other group. Every clothing entry remains represented, and counts respond to
the current search. This does not infer clothing type from item names.

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
mode and saved state. Resident/island form drafts survive language changes; saving
or navigating applies valid drafts. The harness also covers resident/house replacement,
full-byte undo/redo, invalid duplicate replacements and rollback of failed specialized tools.

For an installed Windows WebView2 runtime, the native smoke script also checks the
actual C# bridge, language switching, paging, full screen and origin restriction.
It also opens the real embedded villager gallery, cancels an unchanged specialized
editor and verifies that a turnip-price edit can be undone from the workspace:

```powershell
dotnet run --project NHSE.Workspace.Tests -c Release -- --fixture artifacts/workspace-fixture
pwsh -NoProfile -STA -File NHSE.Tests/Workspace/Smoke-Workspace.ps1 `
  -BuildDirectory NHSE.WinForms/bin/Release/net10.0-windows `
  -FixtureDirectory artifacts/workspace-fixture `
  -OutputDirectory artifacts/workspace-native
pwsh -NoProfile -STA -File NHSE.Tests/Workspace/Smoke-VillagerPicker.ps1 `
  -BuildDirectory NHSE.WinForms/bin/Release/net10.0-windows `
  -OutputDirectory artifacts/workspace-native
```

Only the generated fixture is used. No actual console or user save is required.
The picker smoke check uses in-memory residents and verifies cancellation, resident
switching and replacement selection without modifying the source resident data.
