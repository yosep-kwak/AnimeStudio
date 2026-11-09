param([Parameter(Mandatory)][string]$CliDll)
$ErrorActionPreference = 'Stop'
$output = dotnet $CliDll --capabilities-json
if ($LASTEXITCODE -ne 0 -or @($output).Count -ne 1) { throw 'Expected one successful JSON response.' }
$report = $output | ConvertFrom-Json
if ($report.schema_version -ne 1 -or $report.program -ne 'AnimeStudio.CLI') { throw 'Invalid capabilities identity.' }
if (!$report.runtime.framework -or !$report.runtime.architecture -or !$report.assembly_version) { throw 'Missing runtime identity.' }
if ('Raw' -notin $report.asset_export.modes -or 'JSON' -notin $report.asset_map.formats) { throw 'Existing CLI modes were omitted.' }
if ($report.native_export -ne 'unprobed' -or $report.clip_ir -ne 'unsupported') { throw 'Advertised runtime coverage was overstated.' }
$failure = dotnet $CliDll --capabilities-json unexpected
if ($LASTEXITCODE -ne 2 -or ($failure | ConvertFrom-Json).error.code -ne 'unexpected_arguments') { throw 'Expected a structured usage error.' }
