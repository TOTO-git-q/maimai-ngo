# Replace Sinmai_Data\resources.assets with the resources.assets.new a patch tool wrote. ASCII only.
$d = 'D:\maimai\Package\Sinmai_Data'
# a crashed game can linger as a 0-thread zombie; only a live process counts
if (Get-Process -Name Sinmai -ErrorAction SilentlyContinue | Where-Object { $_.Threads.Count -gt 0 }) { throw 'game is running: not replacing assets' }
Copy-Item "$d\resources.assets.new" "$d\resources.assets" -Force
Remove-Item "$d\resources.assets.new"
Write-Host ('installed ' + (Get-Item "$d\resources.assets").Length + ' bytes')
