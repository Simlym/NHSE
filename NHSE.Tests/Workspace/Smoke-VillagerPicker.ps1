#requires -Version 7.6
param([Parameter(Mandatory)][string]$BuildDirectory, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::SetHighDpiMode([System.Windows.Forms.HighDpiMode]::SystemAware) | Out-Null
foreach ($name in 'Microsoft.Web.WebView2.Core','Microsoft.Web.WebView2.WinForms','NHSE.Core','NHSE.Villagers','NHSE.Injection','NHSE.Sprites','NHSE.WinForms') {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory "$name.dll")) | Out-Null
}
[Microsoft.Web.WebView2.Core.CoreWebView2Environment]::SetLoaderDllFolderPath((Join-Path $BuildDirectory 'runtimes/win-x64/native'))
$assembly=[NHSE.WinForms.WorkspaceForm].Assembly
[System.Runtime.CompilerServices.RuntimeHelpers]::RunClassConstructor($assembly.GetType('NHSE.WinForms.Program').TypeHandle)
$text=$assembly.GetType('NHSE.WinForms.WorkspaceText')
$settingsPath=Join-Path $BuildDirectory 'settings.json'
$settingsBefore=if(Test-Path -LiteralPath $settingsPath){[IO.File]::ReadAllBytes($settingsPath)}else{$null}
$text.GetMethod('SetLanguage').Invoke($null,@('zhs')) | Out-Null
$flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
function Pump { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 }
function Await($task) { $watch=[Diagnostics.Stopwatch]::StartNew(); while(!$task.IsCompleted){if($watch.Elapsed.TotalSeconds -gt 30){throw 'Browser task timed out'};Pump};$task.GetAwaiter().GetResult() }
function Js([string]$code){(Await ($browser.CoreWebView2.ExecuteScriptAsync($code))) | ConvertFrom-Json}
function Until([string]$code){$watch=[Diagnostics.Stopwatch]::StartNew();do{Pump;if(Js $code){return}}while($watch.Elapsed.TotalSeconds -lt 20);throw "Timed out: $code"}
try {
    $residents=[NHSE.Core.IVillager[]]::new(10)
    0..9 | ForEach-Object { $residents[$_]=[NHSE.Core.Villager2]::new([byte[]]::new([NHSE.Core.Villager2]::SIZE)); $residents[$_].Species=35 }
    $residents[0]=[NHSE.Core.Villager2]::new([NHSE.Villagers.VillagerResources]::GetVillager('ant00').Villager)
    $residents[1]=[NHSE.Core.Villager2]::new([NHSE.Villagers.VillagerResources]::GetVillager('cat00').Villager)
    $before=[Convert]::ToHexString($residents[0].Write())
    foreach($action in 'cancel','edit','replace'){
        $form=[NHSE.WinForms.WorkspaceForm]::new($false,$residents,0)
        try{
            $form.Opacity=0;$form.ShowInTaskbar=$false;$form.Show()
            $browser=$form.GetType().GetField('Browser',$flags).GetValue($form)
            $watch=[Diagnostics.Stopwatch]::StartNew()
            while($null -eq $browser.CoreWebView2){if($watch.Elapsed.TotalSeconds -gt 30){throw 'WebView2 unavailable'};Pump}
            Until "document.querySelectorAll('.villager-tile').length > 0 && !document.querySelector('[aria-busy=true]')"
            if((Js "document.querySelectorAll('.resident-chip').length") -ne 10){throw 'Resident strip is incomplete'}
            if($action -eq 'cancel'){
                Js "Array.from(document.querySelectorAll('.villager-species button')).find(b=>b.textContent.trim().startsWith('猫')).click()" | Out-Null
                Until "!document.querySelector('[aria-busy=true]') && document.querySelector('.villager-species button.active').textContent.includes('猫')"
                Js "Array.from(document.querySelectorAll('.villager-tile')).find(b=>!b.disabled&&!b.querySelector('.villager-owned')).click()" | Out-Null
                $stream=[IO.File]::Create((Join-Path $OutputDirectory 'villager-picker-zhs.png'))
                try{ Await ($browser.CoreWebView2.CapturePreviewAsync([Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat]::Png,$stream)) | Out-Null }finally{$stream.Dispose()}
                $code="document.querySelector('.villager-picker-heading button').click()"
            }elseif($action -eq 'edit'){
                Js "document.querySelectorAll('.resident-chip')[1].click()" | Out-Null
                $code="document.querySelectorAll('.villager-preview button')[1].click()"
            }else{
                Js "let q=document.querySelector('.villager-filters input');q.value='cat05';q.dispatchEvent(new Event('input',{bubbles:true}));true" | Out-Null
                Until "document.querySelectorAll('.villager-tile').length===1 && !document.querySelector('[aria-busy=true]')"
                Js "document.querySelector('.villager-tile').click()" | Out-Null
                $code="document.querySelector('.villager-preview button.primary').click()"
            }
            try{Js $code | Out-Null}catch{if(!$form.IsDisposed){throw}}
            $watch=[Diagnostics.Stopwatch]::StartNew();while(!$form.IsDisposed -and $form.Visible){if($watch.Elapsed.TotalSeconds -gt 10){throw 'Picker did not close'};Pump}
            if($action -eq 'cancel' -and $form.PickedVillager){throw 'Cancel selected a replacement'}
            if($action -eq 'edit' -and ($form.PickedResidentIndex -ne 1 -or $form.PickedVillager)){throw 'Resident selection failed'}
            if($action -eq 'replace' -and ($form.PickedVillager -ne 'cat05' -or $form.PickedResidentIndex -ne 0)){throw 'Replacement selection failed'}
            if([Convert]::ToHexString($residents[0].Write()) -ne $before){throw 'Browsing modified resident data'}
            "PASS real villager picker: $action; source resident unchanged"
        }finally{$form.Dispose()}
    }
}finally{
    if($null -ne $settingsBefore){[IO.File]::WriteAllBytes($settingsPath,$settingsBefore)}elseif(Test-Path -LiteralPath $settingsPath){Remove-Item -LiteralPath $settingsPath}
}
