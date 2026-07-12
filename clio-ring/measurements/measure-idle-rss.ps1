$exe = "publish\win-x64\ClioLauncher.Desktop.exe"
$p = Start-Process -FilePath $exe -PassThru
$t0 = Get-Date
Start-Sleep -Seconds 60
$p.Refresh()
$ws = [math]::Round($p.WorkingSet64/1MB,1)
$pw = [math]::Round($p.PeakWorkingSet64/1MB,1)
$priv = [math]::Round($p.PrivateMemorySize64/1MB,1)
"idle_after_60s_workingset_MB=$ws" | Tee-Object -FilePath measurements\idle-rss.txt
"idle_after_60s_peak_workingset_MB=$pw" | Tee-Object -FilePath measurements\idle-rss.txt -Append
"idle_after_60s_private_MB=$priv" | Tee-Object -FilePath measurements\idle-rss.txt -Append
"machine=$env:COMPUTERNAME runtime=net10.0 rid=win-x64 build=Release-NativeAOT-selfcontained" | Tee-Object -FilePath measurements\idle-rss.txt -Append
try { $p.CloseMainWindow() | Out-Null; Start-Sleep 2 } catch {}
try { if(!$p.HasExited){ $p.Kill() } } catch {}
