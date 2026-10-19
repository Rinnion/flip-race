param([Parameter(Mandatory=$true)][string]$Scope,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$core=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets/Scripts/Core') -Filter '*.cs' | ForEach-Object FullName)
$audit=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
$hashes=Get-FileHash -Algorithm SHA256 ($core+$audit)
Add-Type -Path ($core+$audit)
$original=[Console]::Out
$writer=[IO.StringWriter]::new()
$failure=$null
[Console]::SetOut($writer)
try { [TwoModesIndependentAudit]::Run($Scope) } catch { $failure=$_.Exception.Message } finally { [Console]::SetOut($original) }
$after=Get-FileHash -Algorithm SHA256 ($core+$audit)
if(($hashes.Hash -join ',') -ne ($after.Hash -join ',')) { $failure='Sources changed during execution; this is not a frozen checkpoint. '+$failure }
$header='Scope: '+$Scope+"`n"+(($hashes|ForEach-Object { $_.Path.Substring($projectRoot.Length+1).Replace('\','/')+': '+$_.Hash }) -join "`n")+"`n"
$body=$header+$writer.ToString().Replace("`r`n","`n").TrimEnd()+"`n"
if($failure){$body+='FAILED: '+$failure+"`n"}
[IO.File]::WriteAllText((Join-Path $projectRoot $OutputPath),$body,[Text.UTF8Encoding]::new($false))
Write-Output $writer.ToString()
if($failure){throw $failure}
