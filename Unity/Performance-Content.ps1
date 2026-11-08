function Get-PerformanceContentHash([string]$PlayerRoot) {
    $dataRoot = Join-Path $PlayerRoot 'BaiaDoSol_Data'
    $dataPrefix = [IO.Path]::GetFullPath($dataRoot).TrimEnd('\') + '\'
    $entries = foreach ($file in Get-ChildItem -LiteralPath $dataRoot -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($dataPrefix.Length)
        if ($relative -in @('Managed\Assembly-CSharp.dll','Managed\Assembly-CSharp.pdb','Managed\Assembly-CSharp.dll.mdb')) { continue }
        $relative + ':' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))))).Replace('-','') }
    finally { $sha.Dispose() }
}
