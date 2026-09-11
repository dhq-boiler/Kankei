param([string]$Tool = 'uia_find_elements', [string]$ArgumentsJson = '{}', [switch]$Taskbar, [long]$WindowHandle = 0)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Off
$start = [Diagnostics.ProcessStartInfo]::new('C:\Git\JidoDebugger\src\JidoDebugger.Mcp\bin\x64\Debug\net10.0-windows10.0.19041.0\jidodebugger-mcp.exe')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$server = [Diagnostics.Process]::Start($start)
$errors = $server.StandardError.ReadToEndAsync()
$script:requestId = 0
function Rpc($method, $parameters) {
    $script:requestId++
    $server.StandardInput.WriteLine((@{jsonrpc='2.0'; id=$script:requestId; method=$method; params=$parameters} | ConvertTo-Json -Depth 30 -Compress))
    $server.StandardInput.Flush()
    do {
        $read = $server.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(20000)) { throw 'JidoDebugger response timed out.' }
        if ($null -eq $read.Result) { throw 'JidoDebugger closed stdout.' }
        $response = $read.Result | ConvertFrom-Json
    } while ($response.id -ne $script:requestId)
    if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
    return $response.result
}
try {
    $null = Rpc 'initialize' @{protocolVersion='2024-11-05'; capabilities=@{}; clientInfo=@{name='Kankei UI verification'; version='1'}}
    $server.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $server.StandardInput.Flush()
    if ($Tool -eq 'list') {
        (Rpc 'tools/list' @{}).tools | Where-Object name -in @('uia_attach_title','uia_find_elements','uia_invoke','uia_set_value','uia_get_tree') | ConvertTo-Json -Depth 30
    } else {
        if ($Taskbar) {
            Add-Type 'using System; using System.Runtime.InteropServices; public static class TrayLookup { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title); }'
            $WindowHandle = [TrayLookup]::FindWindow('Shell_TrayWnd', $null).ToInt64()
        }
        $attached = if ($WindowHandle) { Rpc 'tools/call' @{name='uia_attach_hwnd'; arguments=@{hwnd=$WindowHandle}} }
            else { Rpc 'tools/call' @{name='uia_attach_title'; arguments=@{windowTitle='Kankei — 配置の保存・復元'}} }
        if ($attached.isError) { throw ($attached | ConvertTo-Json -Depth 10) }
        $info = $attached.content[0].text | ConvertFrom-Json
        $steps = if ($Tool -eq 'batch') { ConvertFrom-Json -AsHashtable $ArgumentsJson } else { @(@{name=$Tool; arguments=(ConvertFrom-Json -AsHashtable $ArgumentsJson)}) }
        foreach ($step in $steps) {
            $step.arguments.sessionId = $info.sessionId
            $result = Rpc 'tools/call' @{name=$step.name; arguments=$step.arguments}
            $result | ConvertTo-Json -Depth 30
            if ($result.isError) { break }
            if ($step.name -like 'uia_attach_*' -or $step.name -like 'attach_to_*') { $info = $result.content[0].text | ConvertFrom-Json }
        }
    }
} finally {
    $server.StandardInput.Close()
    if (-not $server.WaitForExit(3000)) { $server.Kill($true); $server.WaitForExit() }
    $server.Dispose()
}
