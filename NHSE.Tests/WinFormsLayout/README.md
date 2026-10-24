# WinForms DPI layout verification

## Cause and scope

Most independent dialogs used `AutoScaleMode.Inherit`. A dialog's `Owner` is not
its parent container, so opening it from the DPI-aware main window did not scale
its client area and controls. Text could grow while button bounds stayed fixed.
Nested user controls also needed their own font scaling to size their contents.

The fix uses explicit `Font` scaling with each Designer's existing font-metric
baseline (`6 x 13` or `8 x 16`). Recently rewritten forms without a baseline use
`7 x 15`. These are character measurements, not DPI values. Existing fonts and
event handlers remain unchanged. `Main` retains its existing DPI scaling.

- Inventory: `Subforms/PlayerItemEditor.cs` and `.Designer.cs`.
- Single item: `Subforms/SingleItemEditor.cs` and `.Designer.cs`.
- Shared item fields: `Controls/ItemEditor.cs` and `.Designer.cs`.
- Main editor villagers: `Controls/VillagerEditor.cs` and `.Designer.cs`.
- The inventory client area grows from `1002 x 431` to `1044 x 461`.
- The single-item client area grows from `181 x 382` to `246 x 461`.
- Both item controls receive `222 x 400` of space; bottom buttons have a
  6-pixel gap above and 12 pixels below at the design baseline.
- Resizable dialogs take their minimum outer size from their scaled initial
  `Size` after layout, including the current window borders and title bar.
- Item field groups stay vertical and scroll when necessary. Explicit numeric
  field margins prevent inflated default margins from wrapping the input rows.
- Runtime-generated inventory slots convert logical dimensions to device pixels;
  sprite export dimensions and all save editing behavior remain unchanged.
- Slot markup is rendered at its display resolution and DPI, avoiding a second
  enlargement of text from a fixed 64-pixel bitmap. Corner values use alignment
  instead of hardcoded 12-pixel offsets.
- The villager control scales its children independently and permits scrolling;
  the main editor has a minimum initial size to protect its tabs.
- Startup explicitly selects the project's existing `SystemAware` DPI mode.

## Automated layout checks

Run on Windows with PowerShell 7.6 / .NET 10. Each invocation needs a fresh
process because the test compiles temporary layout types into that process:

```powershell
foreach ($language in 'en', 'zhs') {
    foreach ($scale in 1, 1.25, 1.5) {
        pwsh -NoProfile -STA -File NHSE.Tests/WinFormsLayout/Verify-Layout.ps1 -Scale $scale -Language $language
    }
}
pwsh -NoProfile -STA -File NHSE.Tests/WinFormsLayout/Verify-Layout.ps1 -UseSystemDpi -Language zhs
# After a Release build, exercise the actual item form constructors:
pwsh -NoProfile -STA -File NHSE.Tests/WinFormsLayout/Smoke-ItemForms.ps1
```

The script compiles the actual 33 subform Designers, the main Editor Designer,
and shared control Designers
with empty constructors, without save data, resource images, or event handlers.
It uses the actual inventory slot generation code. It checks visible button
bounds and text dimensions, item label/input bounds, and minimum-size behavior.
It mounts the villager control inside the actual main-editor tab and checks its
control bounds and label dimensions, including real system DPI (200% here).
Text reports and item-window PNGs go to the temporary `nhse-layout-*` directories.
Failures produce a nonzero exit status. `-Source original` compares against HEAD.

The scale matrix simulates 100%, 125%, and 150% **font metrics** at virtual 96 DPI,
including explicit control fonts and inventory slot sizes. `-UseSystemDpi` instead
checks the real system DPI. These checks isolate layout; they do not run save
editing, instantiate the real form constructors, or replace real display testing.

The separate smoke script loads the Release build, constructs the real single-item
and inventory forms with synthetic in-memory items, and checks their bottom buttons.
It covers English/Chinese, ordinary items, recipes, flower buds, and 10 x 4 / 10 x 5
inventories populated with nonzero counts and flags. It verifies that markup bitmap
dimensions and DPI match the displayed slots and captures a populated inventory.
It disposes the forms without saving or connecting to a console. Use
`-BuildDirectory <path>` to test a separately built preview.

Validation for this change: Release build succeeded with zero warnings/errors;
all six English/Chinese font-metric runs passed (34 forms, 97 top-level buttons
per run). The system-DPI layout run and all ten real-constructor smoke cases also
passed at 192 DPI (200%). Actual Windows 100%/125%/150% display settings and the
Application compatibility override still require the manual checks below.

## Manual display checks

1. Build `NHSE.WinForms/NHSE.WinForms.csproj` with the .NET 10 SDK.
2. Set Windows scaling to 100%, 125%, 150%, then 200%, restarting NHSE each time.
3. Repeat with the Windows high-DPI compatibility override set to Application.
4. Check both English and Simplified Chinese. Open inventory (10 x 4 and 10 x 5),
   storage, recycle box, single-item, field-item, house, achievement, flag, recipe,
   reaction, mail, villager, settings, and SysBot dialogs.
5. Check the main editor's Villagers tab: labels, number boxes, picture, and buttons
   must not overlap or crop text. Check inventory count/usage/flag text separately.
   Confirm bottom buttons are fully visible and resizable windows stop before
   shrinking below their initial content size. Check labels, numeric inputs,
   item paging, scrolling, and alternate tabs.
6. In the item editor, select ordinary items, flowers, recipes, fossils, wrapped
   items, and house extensions to exercise the groups whose visibility changes.
   Confirm the lower groups remain reachable without covering the action buttons.
7. Cancel without writing the save while doing visual checks.

System-aware scaling targets the display DPI present at startup; moving a running
window between monitors with different DPI is outside this change's scope.
