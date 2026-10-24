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
    $sessionType = $assembly.GetType('NHSE.WinForms.WorkspaceSession')
    $session = [Activator]::CreateInstance($sessionType, @($save))
    $sessionField.SetValue($form, $session)
    $response = Rpc 'bootstrap'
    if (!$response.ok -or $response.data.session.players.Count -ne 1) { throw 'Synthetic session snapshot failed' }
    $before = $save.Players[0].Personal.Bag[0].RawValue
    $id = $save.Players[0].Personal.Bag[0].ItemId
    $response = Rpc 'setItem' @{ player = 0; container = 'pocket'; index = 20; itemId = $id; quantity = 999999 }
    if ($response.ok -or $save.Players[0].Personal.Bag[0].RawValue -ne $before) { throw 'Invalid quantity reached the save' }
    $response = Rpc 'setItem' @{ player = 0; container = 'pocket'; index = 20; itemId = $id; quantity = 2 }
    if (!$response.ok -or !$response.data.session.dirty -or $save.Players[0].Personal.Bag[0].Count -ne 1) { throw 'Native item command failed' }
    $response = Rpc 'undo'
    if (!$response.ok -or $save.Players[0].Personal.Bag[0].RawValue -ne $before) { throw 'Native undo did not restore exact item bytes' }
    'PASS real bridge validates item quantities, writes one-based quantities and restores all bytes on undo'
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
