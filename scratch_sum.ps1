$bytes = [System.IO.File]::ReadAllBytes('D:\CodeBase\AgentCore-Main\AgentCore\bin\Debug\net8.0\AgentCore.dll')
$asm = [System.Reflection.Assembly]::Load($bytes)
try {
    $types = $asm.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
}
foreach ($t in $types) {
    if ($t.Name -like '*Summary*') {
        Write-Host $t.FullName
    }
}