#requires -Version 7.6
param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$FixtureDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
foreach ($name in 'Microsoft.Web.WebView2.Core', 'Microsoft.Web.WebView2.WinForms', 'NHSE.Core', 'NHSE.Injection', 'NHSE.Sprites', 'NHSE.Villagers', 'NHSE.WinForms') {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory "$name.dll")) | Out-Null
}
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic,Public'
$assembly = [NHSE.WinForms.WorkspaceForm].Assembly
[System.Runtime.CompilerServices.RuntimeHelpers]::RunClassConstructor($assembly.GetType('NHSE.WinForms.Program').TypeHandle)
$sessionType = $assembly.GetType('NHSE.WinForms.WorkspaceSession')
$targetType = $sessionType.GetNestedType('MapTarget', [System.Reflection.BindingFlags]'NonPublic')
# Only the supplied synthetic fixture is read; rendering must leave its bytes unchanged.
$save = [NHSE.Core.HorizonSave]::FromFolder($FixtureDirectory)
$raw = [NHSE.Core.EncryptedFilePair].GetField('RawData', $flags).GetValue($save.Main)
$before = [System.Security.Cryptography.SHA256]::HashData([byte[]]$raw)
$session = [Activator]::CreateInstance($sessionType, @($save))
$form = [NHSE.WinForms.WorkspaceForm]::new($false)
try {
    $form.GetType().GetField('Session', $flags).SetValue($form, $session)
    $render = $form.GetType().GetMethod('MapWorkspace', $flags)
    foreach ($mode in 'terrain', 'items', 'buildings', 'acres', 'markers') {
        foreach ($acre in 10, 0, 71) {
            foreach ($layer in 0, 1) {
                $target = [Activator]::CreateInstance($targetType, @($mode, $acre, $layer, 0, 0, 0, 0))
                $result = $render.Invoke($form, @($target, $true))
                # Workspace images must contain terrain, without dense diagnostic labels.
                $cleanEditor = [NHSE.Core.MapEditor]::FromSaveFile($save.Main)
                $cleanEditor.ViewScale = 16
                $cleanEditor.Mutator.View.SetViewToAcre($acre)
                $cleanRenderer = [NHSE.Sprites.MapRenderer]::new($cleanEditor)
                $cleanStream = [System.IO.MemoryStream]::new()
                try {
                    $selectedBuilding = if ($mode -eq 'buildings') { 0 } else { -1 }
                    $cleanRenderer.UpdateViewportTerrain($form.Font, 180, 0, $selectedBuilding, $false, $false).Save($cleanStream, [System.Drawing.Imaging.ImageFormat]::Png)
                    $expected = [Convert]::ToBase64String($cleanStream.ToArray())
                    if ($result.Viewport.Split(',')[1] -ne $expected) { throw "Workspace viewport contains diagnostic overlays for $mode / $acre / $layer" }
                } finally { $cleanStream.Dispose(); $cleanRenderer.Dispose() }
                foreach ($name in 'Overview', 'Viewport', 'ItemImage') {
                    $bytes = [Convert]::FromBase64String($result.$name.Split(',')[1])
                    $stream = [System.IO.MemoryStream]::new($bytes)
                    $image = [System.Drawing.Bitmap]::new($stream)
                    try {
                        $width = if ($name -eq 'Overview') { 288 } else { 512 }
                        $height = if ($name -eq 'Overview') { 256 } else { 512 }
                        if ($image.Width -ne $width -or $image.Height -ne $height) { throw "Incorrect $name size for $mode / $acre / $layer" }
                    } finally { $image.Dispose(); $stream.Dispose() }
                }
                $selection = $render.Invoke($form, @($target, $false))
                if ($selection.Overview -or $selection.Viewport -or $selection.ItemImage -or $selection.Cells.Length -ne $result.Cells.Length) {
                    throw 'Cell selection should return editing data without rendering images'
                }
            }
        }
    }
    # The native map editor shares this renderer and changes scale when resized.
    $editor = [NHSE.Core.MapEditor]::FromSaveFile($save.Main)
    $editor.Mutator.View.SetViewToAcre(10)
    foreach ($scale in 1, 3, 8, 16, 24) {
        $editor.ViewScale = $scale
        $renderer = [NHSE.Sprites.MapRenderer]::new($editor)
        try {
            $terrain = $renderer.UpdateViewportTerrain($form.Font, 0, 0)
            $items = $renderer.UpdateViewportItems(-1)
            if ($terrain.Width -ne 32 * $scale -or $terrain.Height -ne 32 * $scale -or $terrain.Size -ne $items.Size) {
                throw "Terrain and item layers do not match at scale $scale"
            }
        } finally { $renderer.Dispose() }
    }
    $after = [System.Security.Cryptography.SHA256]::HashData([byte[]]$raw)
    if ([Convert]::ToHexString($before) -ne [Convert]::ToHexString($after)) { throw 'Rendering changed save bytes' }
    'PASS workspace map renders all five modes, both layers and interior/ocean acres without changing save bytes'
    'PASS terrain and item layers match at scales 1, 3, 8, 16 and 24'
} finally { $form.Dispose() }
