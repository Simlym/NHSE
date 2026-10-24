#requires -Version 7.6
param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::SetHighDpiMode([System.Windows.Forms.HighDpiMode]::SystemAware) | Out-Null
[System.Windows.Forms.Application]::EnableVisualStyles()
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$build = if ($BuildDirectory) { $BuildDirectory } else { Join-Path $repo 'NHSE.WinForms\bin\Release\net10.0-windows' }
foreach ($assembly in 'NHSE.Core','NHSE.Injection','NHSE.Sprites','NHSE.Villagers','NHSE.WinForms') {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $build "$assembly.dll")) | Out-Null
}
$flowerId = 0
foreach ($id in 0..15000) { if ([NHSE.Core.ItemInfo]::GetItemKind([ushort]$id).ToString() -eq 'Kind_FlowerBud') { $flowerId = $id; break } }
$cases = 0
foreach ($language in 'en','zhs') {
    [NHSE.Core.GameInfo]::SetLanguage2Char([NHSE.Core.GameLanguage]::GetLanguageIndex($language)) | Out-Null
    foreach ($id in 1,0x16A2,$flowerId) {
        $item = [NHSE.Core.Item]::new()
        $item.ItemId = $id
        $dialog = [NHSE.WinForms.SingleItemEditor]::new($item)
        try {
            $dialog.Opacity = 0
            $dialog.ShowInTaskbar = $false
            $dialog.Show()
            [System.Windows.Forms.Application]::DoEvents()
            foreach ($name in 'B_Save','B_Cancel') {
                $button = $dialog.Controls.Find($name,$true)[0]
                if (!$dialog.ClientRectangle.Contains($button.Bounds)) { throw "$name is clipped" }
            }
            "Actual SingleItemEditor: language=$language; item=$id; DPI=$($dialog.DeviceDpi); client=$($dialog.ClientSize)"
            $cases++
        } finally { $dialog.Dispose() }
    }
    foreach ($rows in 4,5) {
        $items = [NHSE.Core.Item[]]::new(10 * $rows)
        for ($i = 0; $i -lt $items.Length; $i++) {
            $items[$i] = [NHSE.Core.Item]::new()
            $items[$i].ItemId = $flowerId
            $items[$i].Count = 43
            $items[$i].UseCount = 128
        }
        $items[0].SystemParam = 128
        $items[0].AdditionalParam = 255
        $dialog = [NHSE.WinForms.PlayerItemEditor]::new($items,10,$rows,$false)
        try {
            $dialog.Opacity = 0
            $dialog.ShowInTaskbar = $false
            $dialog.Show()
            [System.Windows.Forms.Application]::DoEvents()
            foreach ($name in 'B_Save','B_Cancel','B_Dump','B_Load') {
                $button = $dialog.Controls.Find($name,$true)[0]
                if (!$dialog.ClientRectangle.Contains($button.Bounds)) { throw "$name is clipped" }
            }
            $grid = $dialog.Controls.Find('ItemGrid',$true)[0]
            foreach ($slot in $grid.Entries) {
                if ($slot.Image.Size -ne $slot.ClientSize) { throw "Slot markup $($slot.Image.Size) does not match display $($slot.ClientSize); slot=$($slot.Size); DPI=$($slot.DeviceDpi)" }
                if ([Math]::Abs($slot.Image.HorizontalResolution - $slot.DeviceDpi) -gt 1) { throw 'Slot markup DPI is incorrect' }
            }
            if ($rows -eq 4 -and $language -eq 'en') {
                $bitmap = [System.Drawing.Bitmap]::new($dialog.Width, $dialog.Height)
                try {
                    $dialog.DrawToBitmap($bitmap, [System.Drawing.Rectangle]::new([System.Drawing.Point]::Empty, $dialog.Size))
                    $bitmap.Save((Join-Path $build 'inventory-dpi-preview.png'))
                } finally { $bitmap.Dispose() }
            }
            "Actual PlayerItemEditor: language=$language; grid=10x$rows; DPI=$($dialog.DeviceDpi); client=$($dialog.ClientSize)"
            $cases++
        } finally { $dialog.Dispose() }
    }
}
"Passed $cases actual-constructor item-window smoke checks."
