#requires -Version 7.6
param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::SetHighDpiMode([System.Windows.Forms.HighDpiMode]::SystemAware) | Out-Null
[System.Windows.Forms.Application]::EnableVisualStyles()
foreach ($name in 'NHSE.Core','NHSE.Injection','NHSE.Sprites','NHSE.Villagers','NHSE.WinForms') {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory "$name.dll")) | Out-Null
}

function Pump-Layout {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt 300) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 20
    }
}

# Construct a synthetic, in-memory save without reading or writing any user files.
$save = [System.Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([NHSE.Core.MainSave])
$data = [byte[]]::new(0x2000000)
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic,Public'
$revision = [NHSE.Core.RevisionChecker].GetField('RevisionInfo', [System.Reflection.BindingFlags]'Static,NonPublic').GetValue($null)[30]
[NHSE.Core.EncryptedFilePair].GetField('RawData',$flags).SetValue($save,$data)
[NHSE.Core.EncryptedFilePair].GetField('RawHeader',$flags).SetValue($save,[byte[]]::new(0x300))
[NHSE.Core.EncryptedFilePair].GetField('Info',$flags).SetValue($save,$revision)
[NHSE.Core.MainSave].GetField('Offsets',$flags).SetValue($save,[NHSE.Core.MainSaveOffsets]::GetOffsets($revision))
$before = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($data))

$form = [NHSE.WinForms.FieldItemEditor]::new($save)
try {
    $form.Opacity = 0
    $form.ShowInTaskbar = $false
    $form.Show()
    Pump-Layout
    $viewport = $form.Controls.Find('PB_Viewport',$true)[0]
    $hostPanel = $viewport.Parent
    $editor = $form.GetType().GetField('Editor',$flags).GetValue($form)
    $mapCoordinates = $form.GetType().GetMethod('GetMapImageCoordinates',$flags)
    $viewCoordinates = $form.GetType().GetMethod('GetViewCoordinates',$flags)
    $processKey = $form.GetType().GetMethod('ProcessCmdKey',$flags)

    function Assert-Map {
        param([string]$State)
        Pump-Layout
        if ($viewport.Width -ne $viewport.Height -or !$hostPanel.ClientRectangle.Contains($viewport.Bounds)) { throw "Invalid viewport bounds: $State" }
        if ($viewport.Image.Width -ne $editor.ViewScale * 32 -or $viewport.BackgroundImage.Size -ne $viewport.Image.Size) { throw "Render layers do not match at $State" }
        if ($viewport.ClientSize -ne $viewport.Image.Size) { throw "Map image is stretched instead of rendered at native resolution at $State" }
        foreach ($y in 0..31) {
            foreach ($x in 0..31) {
                $px = [int][Math]::Floor((2 * $x + 1) * $viewport.ClientSize.Width / 64)
                $py = [int][Math]::Floor((2 * $y + 1) * $viewport.ClientSize.Height / 64)
                $mouse = [System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::None,0,$px,$py,0)
                $point = $viewCoordinates.Invoke($form,@($mouse))
                if ($point.Item1 -ne $x -or $point.Item2 -ne $y) { throw "Wrong map tile at $State : expected $x,$y; actual $point" }
            }
        }
        foreach ($name in 'B_Save','B_Cancel','B_FullScreen') {
            $button = $form.Controls.Find($name,$true)[0]
            $bounds = $form.RectangleToClient($button.Parent.RectangleToScreen($button.Bounds))
            if (!$form.ClientRectangle.Contains($bounds)) { throw "Action button clipped at $State" }
        }
        "${State}: client=$($form.ClientSize); viewport=$($viewport.ClientSize); render=$($viewport.Image.Size); tile pixels=$($editor.ViewScale)"
    }

    Assert-Map 'Maximized'
    $large = $viewport.Width
    $bitmap = [System.Drawing.Bitmap]::new($form.Width,$form.Height)
    try {
        $form.DrawToBitmap($bitmap,[System.Drawing.Rectangle]::new([System.Drawing.Point]::Empty,$form.Size))
        $bitmap.Save((Join-Path $BuildDirectory 'map-maximized.png'))
    } finally { $bitmap.Dispose() }
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.Size = $form.MinimumSize
    Assert-Map 'Minimum size'
    if ($viewport.Width -ge $large) { throw 'Map did not shrink with window' }
    $normalBounds = $form.Bounds
    $keyArgs = [object[]]@([System.Windows.Forms.Message]::new(),[System.Windows.Forms.Keys]::F11)
    $processKey.Invoke($form,$keyArgs) | Out-Null
    Assert-Map 'Normal window to full screen'
    $processKey.Invoke($form,$keyArgs) | Out-Null
    Assert-Map 'F11 normal window restore'
    if ($form.WindowState -ne [System.Windows.Forms.FormWindowState]::Normal -or $form.Bounds -ne $normalBounds) { throw 'F11 did not restore the normal window bounds' }
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Maximized
    $keyArgs = [object[]]@([System.Windows.Forms.Message]::new(),[System.Windows.Forms.Keys]::F11)
    $processKey.Invoke($form,$keyArgs) | Out-Null
    Assert-Map 'F11 full screen'
    if ($form.FormBorderStyle -ne [System.Windows.Forms.FormBorderStyle]::None) { throw 'F11 did not enter full screen' }
    $keyArgs[1] = [System.Windows.Forms.Keys]::Escape
    $processKey.Invoke($form,$keyArgs) | Out-Null
    Assert-Map 'Escape restore'
    if ($form.FormBorderStyle -ne [System.Windows.Forms.FormBorderStyle]::Sizable) { throw 'Escape did not restore window border' }

    $overview = $form.Controls.Find('PB_Map',$true)[0]
    $cursor = [System.Drawing.Point]::new([int]($overview.ClientSize.Width / 2),[int]($overview.ClientSize.Height / 2))
    $point = $mapCoordinates.Invoke($form,@($cursor))
    if ([Math]::Abs($point.X - $overview.Image.Width / 2) -gt 1 -or [Math]::Abs($point.Y - $overview.Image.Height / 2) -gt 1) { throw 'Overview coordinates are incorrect' }
    $form.GetType().GetMethod('PB_Map_MouseDown',$flags).Invoke($form,@($overview,[System.Windows.Forms.MouseEventArgs]::new([System.Windows.Forms.MouseButtons]::Left,1,$cursor.X,$cursor.Y,0))) | Out-Null
    Assert-Map 'Overview navigation'
    $after = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($data))
    if ($after -ne $before) { throw 'Window layout or navigation changed save bytes' }
    'Map resize, full-screen, layer alignment, all 1024 tile coordinates, overview navigation, and unchanged save bytes passed.'
} finally { $form.Close(); $form.Dispose() }
