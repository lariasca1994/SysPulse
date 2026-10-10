# Escanea paquetes que se van a publicar (zip, apk, exe) buscando secretos del .env y datos personales.
# Revisa cada archivo interno en UTF-8 y UTF-16 (los .NET guardan texto en UTF-16). No muestra los valores.
# Uso: powershell -File tools/escanear-secretos.ps1 -Paquetes artifacts\release\*.zip, artifacts\release\*.apk
param(
    [Parameter(Mandatory)] [string[]] $Paquetes,
    [string] $Env = (Join-Path $PSScriptRoot "..\.env"),
    [string[]] $Extra = @()
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Valores a buscar: todos los del .env de 8+ caracteres, cada chat id por separado y datos adicionales.
$buscar = @{}
foreach ($l in [IO.File]::ReadAllLines($Env, [Text.Encoding]::UTF8)) {
    if ($l -notmatch '^\s*([A-Z_]+)=(.*)$') { continue }
    $clave = $Matches[1]; $valor = $Matches[2].Trim().Trim('"')
    # Direcciones públicas (las ve cualquiera que abra la web): no son secretos y el instalador necesita la de la API.
    if ($clave -in "API_URL", "API_PUBLICA", "WEB_URL", "CORS_ORIGIN", "TELEGRAM_BOT_USUARIO", "BREVO_SENDER_NAME") { continue }
    if ($valor.Length -ge 8 -and $valor -notmatch '^https?://localhost') { $buscar["$clave"] = $valor }
    if ($clave -eq "TELEGRAM_FIXED_CHAT_IDS") { $i = 0; foreach ($id in ($valor -split '[,; ]+' | Where-Object { $_ })) { $buscar["CHAT_ID_$i"] = $id; $i++ } }
    # Partes sensibles de la cadena de conexión (usuario y contraseña de la base).
    if ($clave -eq "MONGO_URI" -and $valor -match '//([^:]+):([^@]+)@') { $buscar["MONGO_USUARIO"] = $Matches[1]; $buscar["MONGO_PASSWORD"] = $Matches[2] }
}
$i = 0; foreach ($e in $Extra) { $buscar["EXTRA_$i"] = $e; $i++ }

$patrones = foreach ($par in $buscar.GetEnumerator()) {
    [pscustomobject]@{ Nombre = $par.Key; Utf8 = [Text.Encoding]::UTF8.GetBytes($par.Value); Utf16 = [Text.Encoding]::Unicode.GetBytes($par.Value) }
}

function Contiene([byte[]] $datos, [byte[]] $patron) {
    if ($patron.Length -eq 0 -or $datos.Length -lt $patron.Length) { return $false }
    $primero = $patron[0]
    $limite = $datos.Length - $patron.Length
    $i = [Array]::IndexOf($datos, $primero, 0)
    while ($i -ge 0 -and $i -le $limite) {
        $ok = $true
        for ($j = 1; $j -lt $patron.Length; $j++) { if ($datos[$i + $j] -ne $patron[$j]) { $ok = $false; break } }
        if ($ok) { return $true }
        $i = [Array]::IndexOf($datos, $primero, $i + 1)
    }
    return $false
}

$hallazgos = 0
foreach ($ruta in ($Paquetes | ForEach-Object { Get-ChildItem $_ })) {
    $entradas = @()
    if ($ruta.Extension -in ".zip", ".apk") {
        $zip = [IO.Compression.ZipFile]::OpenRead($ruta.FullName)
        try {
            foreach ($e in $zip.Entries) {
                if ($e.Length -eq 0) { continue }
                $ms = New-Object IO.MemoryStream; $s = $e.Open(); $s.CopyTo($ms); $s.Dispose()
                $entradas += [pscustomobject]@{ Nombre = $e.FullName; Datos = $ms.ToArray() }
            }
        } finally { $zip.Dispose() }
    } else {
        $entradas += [pscustomobject]@{ Nombre = $ruta.Name; Datos = [IO.File]::ReadAllBytes($ruta.FullName) }
    }
    $encontrados = 0
    foreach ($en in $entradas) {
        foreach ($p in $patrones) {
            if ((Contiene $en.Datos $p.Utf8) -or (Contiene $en.Datos $p.Utf16)) { "  ✘ $($ruta.Name) → $($en.Nombre): contiene $($p.Nombre)"; $encontrados++ }
        }
    }
    if ($encontrados -eq 0) { "  ✔ $($ruta.Name): $($entradas.Count) archivo(s) revisados, sin secretos ni datos personales" }
    $hallazgos += $encontrados
}
"Valores buscados: $($patrones.Count) · hallazgos: $hallazgos"
if ($hallazgos -gt 0) { exit 1 }
