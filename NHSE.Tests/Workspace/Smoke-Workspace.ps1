#requires -Version 7.6
param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$FixtureDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::SetHighDpiMode([System.Windows.Forms.HighDpiMode]::SystemAware) | Out-Null
[System.Windows.Forms.Application]::EnableVisualStyles()
foreach ($name in 'Microsoft.Web.WebView2.Core', 'Microsoft.Web.WebView2.WinForms', 'NHSE.Core', 'NHSE.Injection', 'NHSE.Sprites', 'NHSE.Villagers', 'NHSE.WinForms') {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory "$name.dll")) | Out-Null
}
# PowerShell has a different app base than the deployed executable.
[Microsoft.Web.WebView2.Core.CoreWebView2Environment]::SetLoaderDllFolderPath((Join-Path $BuildDirectory 'runtimes/win-x64/native'))
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic,Public'
$assembly = [NHSE.WinForms.WorkspaceForm].Assembly
$settingsPath = Join-Path $BuildDirectory 'settings.json'
$settingsBefore = if (Test-Path -LiteralPath $settingsPath) { [System.IO.File]::ReadAllBytes($settingsPath) } else { $null }
[System.Runtime.CompilerServices.RuntimeHelpers]::RunClassConstructor($assembly.GetType('NHSE.WinForms.Program').TypeHandle)
$form = [NHSE.WinForms.WorkspaceForm]::new($false)
$sessionField = $form.GetType().GetField('Session', $flags)

function Pump { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 }
function Wait-Task($task) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while (!$task.IsCompleted) { if ($timer.Elapsed.TotalSeconds -gt 30) { throw 'WebView task timed out' }; Pump }
    return $task.GetAwaiter().GetResult()
}
function Run-Js([string]$code) { return (Wait-Task ($browser.CoreWebView2.ExecuteScriptAsync($code))) | ConvertFrom-Json }
function Wait-Condition([string]$code) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    do { Pump; if (Run-Js $code) { return } } while ($timer.Elapsed.TotalSeconds -lt 20)
    throw "Condition timed out: $code"
}
function Rpc([string]$command, $payload = @{}) {
    $messageId = 'test-' + [Guid]::NewGuid().ToString('N')
    $message = @{ id = $messageId; command = $command; payload = $payload } | ConvertTo-Json -Compress
    Run-Js "chrome.webview.postMessage($message)" | Out-Null
    Wait-Condition "!!window.__nhseReplies?.['$messageId']"
    return Run-Js "window.__nhseReplies['$messageId']"
}
function Hook-Replies { Run-Js "window.__nhseReplies={};chrome.webview.addEventListener('message',e=>{window.__nhseReplies[e.data.id]=e.data});true" | Out-Null }

try {
    $form.Opacity = 0
    $form.ShowInTaskbar = $false
    $form.Show()
    $browser = $form.GetType().GetField('Browser', $flags).GetValue($form)
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($null -eq $browser.CoreWebView2) {
        if ($timer.Elapsed.TotalSeconds -gt 30) {
            $labels = foreach ($control in $form.Controls) { foreach ($child in $control.Controls) { if ($child -is [System.Windows.Forms.Label]) { $child.Text } } }
            throw ('WebView2 did not initialize: ' + ($labels -join ' / '))
        }
        Pump
    }
    Wait-Condition "!!document.querySelector('h1') && !document.querySelector('[aria-busy=true]')"
    Hook-Replies
    $response = Rpc 'language' @{ language = 'zhs' }
    if (!$response.ok) { throw ($response | ConvertTo-Json) }
    $browser.Reload()
    Wait-Condition "document.querySelector('h1')?.textContent === '让岛屿生活，更合心意。'"
    Hook-Replies
    $stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'home-zhs.png'))
    try { Wait-Task ($browser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png, $stream)) } finally { $stream.Dispose() }
    'PASS Vue home loaded inside the real WebView2 runtime and switched to Chinese'

    foreach ($code in 'en', 'jp', 'de', 'es', 'fr', 'it', 'ko', 'zhs', 'zht') {
        $response = Rpc 'language' @{ language = $code }
        if (!$response.ok -or $response.data.language -ne $code) { throw "Language failed: $code" }
    }
    Rpc 'language' @{ language = 'zhs' } | Out-Null
    'PASS native bridge preserves all nine existing languages'
    $save = [NHSE.Core.HorizonSave]::FromFolder($FixtureDirectory)
    # Add test-only JPEG portraits to verify avatar decoding, without using player photos.
    foreach ($personal in $save.Players.Personal) {
        $bitmap = [System.Drawing.Bitmap]::new(48,48)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $memory = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::SeaGreen)
            $graphics.FillEllipse([System.Drawing.Brushes]::Wheat,15,7,18,18)
            $graphics.FillEllipse([System.Drawing.Brushes]::Wheat,8,27,32,32)
            $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Jpeg)
            $photo = $memory.ToArray()
            $raw = [NHSE.Core.EncryptedFilePair].GetField('RawData',$flags).GetValue($personal)
            [BitConverter]::GetBytes([int]$photo.Length).CopyTo($raw,$personal.Offsets.ProfilePhoto - 4)
            $photo.CopyTo($raw,$personal.Offsets.ProfilePhoto)
        } finally { $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $sessionType = $assembly.GetType('NHSE.WinForms.WorkspaceSession')
    $session = [Activator]::CreateInstance($sessionType, @($save))
    $sessionField.SetValue($form, $session)
    $response = Rpc 'bootstrap'
    if (!$response.ok -or $response.data.session.players.Count -ne $save.Players.Count) { throw 'Synthetic session snapshot failed' }
    if (@($response.data.session.players | Where-Object { $_.avatar -notlike 'data:image/png;base64,*' }).Count -ne 0) { throw 'Player avatar decoding failed' }
    'PASS native snapshots decode the save portraits for avatar-based player switching'
    $before = $save.Players[0].Personal.Bag[0].RawValue
    $id = $save.Players[0].Personal.Bag[0].ItemId
    $response = Rpc 'setItem' @{ player = 0; container = 'pocket'; index = 20; itemId = $id; quantity = 999999 }
    if ($response.ok -or $save.Players[0].Personal.Bag[0].RawValue -ne $before) { throw 'Invalid quantity reached the save' }
    $response = Rpc 'setItem' @{ player = 0; container = 'pocket'; index = 20; itemId = $id; quantity = 2 }
    if (!$response.ok -or !$response.data.session.dirty -or $save.Players[0].Personal.Bag[0].Count -ne 1) { throw 'Native item command failed' }
    $response = Rpc 'undo'
    if (!$response.ok -or $save.Players[0].Personal.Bag[0].RawValue -ne $before) { throw 'Native undo did not restore exact item bytes' }
    'PASS real bridge validates item quantities, writes one-based quantities and restores all bytes on undo'
    $personal = $save.Players[0].Personal
    $oldName = $personal.PlayerName
    $oldIsland = $personal.TownName
    $profile = @{ player=0; name='NativeName'; island='NativeIsle'; wallet=$personal.Wallet.Value; bank=$personal.Bank.Value; miles=$personal.NookMiles.Value; earnedMiles=$personal.TotalNookMiles.Value; pocketCapacity=($personal.PocketCount + $personal.BagCount); storageCapacity=$personal.ItemChestCount }
    $response = Rpc 'setPlayer' $profile
    if (!$response.ok -or $personal.PlayerName -ne 'NativeName' -or @($save.Players | Where-Object { $_.Personal.TownName -ne 'NativeIsle' }).Count -ne 0) { throw 'Native identity editing failed' }
    $response = Rpc 'undo'
    if (!$response.ok -or $personal.PlayerName -ne $oldName -or $personal.TownName -ne $oldIsland) { throw 'Native identity undo failed' }
    'PASS native rename updates related island identities and can be undone'
    $response = Rpc 'catalog' @{ query=''; category='all'; page=0 }
    if (!$response.ok -or $response.data.items.Count -ne 96 -or $response.data.pages -lt 2) { throw 'Full catalog paging failed' }
    $totalItems = $response.data.total
    $response = Rpc 'catalog' @{ query=''; category='all'; page=($response.data.pages - 1) }
    if (!$response.ok -or $response.data.items.Count -eq 0 -or $response.data.total -ne $totalItems) { throw 'Last catalog page failed' }
    $response = Rpc 'catalog' @{ query=''; category='clothing'; page=0 }
    if (!$response.ok -or $response.data.total -eq 0) { throw 'Game-derived clothing filter failed' }
    $clothingTotal = $response.data.total
    $subcategories = @($response.data.subcategories | Where-Object { $_.id -ne 'all' })
    if (($subcategories | Measure-Object -Property count -Sum).Sum -ne $clothingTotal) { throw 'Clothing subcategories lost items' }
    $response = Rpc 'catalog' @{ query=''; category='clothing'; subcategory='tops'; page=0 }
    if (!$response.ok -or $response.data.total -eq 0 -or $response.data.total -ne ($subcategories | Where-Object id -eq 'tops').count) { throw 'Clothing subtype filter failed' }
    "PASS native catalog exposes all $totalItems items with paging and game-derived categories"
    $response = Rpc 'inventory' @{ player = 0; container = 'storage'; page = 9999; query = '' }
    if (!$response.ok -or $response.data.items.Count -gt 60 -or $response.data.page -ge $response.data.pages) { throw 'Native paging failed' }
    'PASS storage paging stays bounded'
    $browser.Reload()
    Wait-Condition "document.querySelector('h1')?.textContent === '岛屿概览' && !document.querySelector('[aria-busy=true]')"
    Hook-Replies
    Run-Js "document.querySelectorAll('.sidebar nav button')[2].click()" | Out-Null
    Wait-Condition "document.querySelectorAll('.item-slot').length > 0 && !document.querySelector('[aria-busy=true]')"
    Run-Js "document.querySelectorAll('.item-slot')[20].click()" | Out-Null
    Wait-Condition "!!document.querySelector('.inspector .quantity-field')"
    $stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'inventory-zhs.png'))
    try { Wait-Task ($browser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png, $stream)) | Out-Null } finally { $stream.Dispose() }
    'PASS real Vue inventory shows synthetic save items and ordinary click selection'
    Run-Js "document.querySelector('.choose-item').click()" | Out-Null
    Wait-Condition "document.querySelectorAll('.catalog-item').length === 96 && !document.querySelector('[aria-busy=true]')"
    $stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'catalog-zhs.png'))
    try { Wait-Task ($browser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png, $stream)) | Out-Null } finally { $stream.Dispose() }
    Run-Js "Array.from(document.querySelectorAll('.catalog-categories button')).find(b=>b.textContent.includes('服饰与配件')).click()" | Out-Null
    Wait-Condition "document.querySelectorAll('.catalog-subcategories button').length > 1 && !document.querySelector('[aria-busy=true]')"
    Run-Js "document.querySelectorAll('.catalog-subcategories button')[1].click()" | Out-Null
    Wait-Condition "document.querySelectorAll('.catalog-subcategories button')[1].classList.contains('active') && !document.querySelector('[aria-busy=true]')"
    $stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'clothing-subcategories-zhs.png'))
    try { Wait-Task ($browser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png, $stream)) | Out-Null } finally { $stream.Dispose() }
    'PASS real clothing catalog filters by game subcategories without losing entries'
    Run-Js "document.querySelector('.catalog-modal .modal-heading button').click()" | Out-Null
    Run-Js "document.querySelectorAll('.sidebar nav button')[1].click()" | Out-Null
    Wait-Condition "!!document.querySelector('input[aria-describedby=player-name-help]')"
    $stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'player-profile-zhs.png'))
    try { Wait-Task ($browser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png, $stream)) | Out-Null } finally { $stream.Dispose() }
    Run-Js "document.querySelectorAll('.player-chip')[1].click()" | Out-Null
    Wait-Condition "document.querySelector('input[aria-describedby=player-name-help]').value === 'Other'"
    'PASS real Vue player profile supports avatar/name switching and editable identity fields'
    $response = Rpc 'fullScreen'
    if (!$response.data.fullScreen -or $form.FormBorderStyle -ne 'None') { throw 'Fullscreen failed' }
    $response = Rpc 'fullScreen'
    if ($response.data.fullScreen -or $form.FormBorderStyle -ne 'Sizable') { throw 'Fullscreen restore failed' }
    'PASS native fullscreen and restore'
    $browser.CoreWebView2.Navigate('https://example.com')
    1..30 | ForEach-Object { Pump }
    if ($browser.CoreWebView2.Source -notlike 'https://nhse.local/*') { throw 'External navigation was not blocked' }
    'PASS external navigation is blocked'
    'All native WebView2 smoke checks passed. Only synthetic save data was used.'
} finally {
    $sessionField.SetValue($form, $null)
    $form.Close()
    $form.Dispose()
    if ($null -ne $settingsBefore) { [System.IO.File]::WriteAllBytes($settingsPath, $settingsBefore) }
    elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath }
}
