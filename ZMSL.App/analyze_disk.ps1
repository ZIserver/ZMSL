$path = "d:\Desktop\ZMSL\ZMSL.App"
Write-Host "--- Top 20 Largest Files ---"
Get-ChildItem -Path $path -Recurse -File | Sort-Object Length -Descending | Select-Object -First 20 @{Name="Size(MB)";Expression={"{0:N2}" -f ($_.Length / 1MB)}}, FullName

Write-Host "`n--- Folder Sizes ---"
Get-ChildItem -Path $path -Directory | ForEach-Object {
    $size = (Get-ChildItem -Path $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    [PSCustomObject]@{
        Name = $_.Name
        SizeMB = [math]::Round($size / 1MB, 2)
    }
} | Sort-Object SizeMB -Descending | Format-Table -AutoSize