$ErrorActionPreference = 'Stop'
$dir = 'C:\Users\D8671~1.KRE\AppData\Local\Temp\claude\C--Projects-clio\4829fbce-9197-4acd-8f4f-c0a08bc00ee0\scratchpad'

function Get-RawDeflate([byte[]]$bytes) {
    $ms = New-Object System.IO.MemoryStream
    $ds = New-Object System.IO.Compression.DeflateStream($ms, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $ds.Write($bytes, 0, $bytes.Length)
    $ds.Dispose()
    $out = $ms.ToArray()
    $ms.Dispose()
    return $out
}

function Get-RawInflate([byte[]]$bytes) {
    $ms = New-Object System.IO.MemoryStream(,$bytes)
    $ds = New-Object System.IO.Compression.DeflateStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
    $outMs = New-Object System.IO.MemoryStream
    $ds.CopyTo($outMs)
    $ds.Dispose()
    $out = $outMs.ToArray()
    $outMs.Dispose()
    return $out
}

# PlantUML's own 6-bit alphabet (for plantuml.com syntax validation only)
$alphabet = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_'
function ConvertTo-PlantUmlEncoding([byte[]]$data) {
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $data.Length; $i += 3) {
        $b1 = $data[$i]
        $b2 = if ($i + 1 -lt $data.Length) { $data[$i + 1] } else { 0 }
        $b3 = if ($i + 2 -lt $data.Length) { $data[$i + 2] } else { 0 }
        [void]$sb.Append($alphabet[($b1 -shr 2) -band 0x3F])
        [void]$sb.Append($alphabet[((($b1 -band 0x3) -shl 4) -bor (($b2 -shr 4) -band 0xF)) -band 0x3F])
        [void]$sb.Append($alphabet[((($b2 -band 0xF) -shl 2) -bor (($b3 -shr 6) -band 0x3)) -band 0x3F])
        [void]$sb.Append($alphabet[$b3 -band 0x3F])
    }
    return $sb.ToString()
}

$result = @()
foreach ($name in @('d1-components', 'd2-install', 'd3-create', 'd4-modify')) {
    $path = Join-Path $dir "$name.puml"
    $text = [System.IO.File]::ReadAllText($path)
    $text = $text -replace "`r`n", "`n"

    # Confluence plantumlcloud macro: Base64( raw-deflate( encodeURIComponent(text) ) )
    $escaped = [Uri]::EscapeDataString($text)
    $macroBlob = [Convert]::ToBase64String((Get-RawDeflate ([Text.Encoding]::UTF8.GetBytes($escaped))))

    # round-trip check
    $back = [Uri]::UnescapeDataString([Text.Encoding]::UTF8.GetString((Get-RawInflate ([Convert]::FromBase64String($macroBlob)))))
    $ok = ($back -eq $text)

    # plantuml.com encoding for syntax validation
    $pumlEnc = ConvertTo-PlantUmlEncoding (Get-RawDeflate ([Text.Encoding]::UTF8.GetBytes($text)))

    $result += [pscustomobject]@{ Name = $name; RoundTrip = $ok; Len = $macroBlob.Length }
    [System.IO.File]::WriteAllText((Join-Path $dir "$name.macro.txt"), $macroBlob)
    [System.IO.File]::WriteAllText((Join-Path $dir "$name.plantumlcom.txt"), $pumlEnc)
}
$result | Format-Table -AutoSize
