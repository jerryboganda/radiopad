function Read-HookPayload {
    $inputText = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($inputText)) {
        return [pscustomobject]@{}
    }

    try {
        return $inputText | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return [pscustomobject]@{ rawInput = $inputText }
    }
}

function Write-HookResult {
    param([Parameter(Mandatory = $true)] $Value)
    $Value | ConvertTo-Json -Depth 24
}

function Get-PropertyValue {
    param($Object, [string] $Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { return $property.Value }
    return $null
}

function Get-FirstString {
    param([object[]] $Values)
    foreach ($value in $Values) {
        if ($value -is [string] -and -not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }
    return ''
}

function Find-NestedString {
    param($Value, [string[]] $Keys, [int] $Depth = 0)
    if ($null -eq $Value -or $Depth -gt 5) { return '' }

    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            $found = Find-NestedString -Value $item -Keys $Keys -Depth ($Depth + 1)
            if ($found) { return $found }
        }
        return ''
    }

    if ($Value -isnot [psobject]) { return '' }

    foreach ($property in $Value.PSObject.Properties) {
        if ($Keys -contains $property.Name -and $property.Value -is [string] -and -not [string]::IsNullOrWhiteSpace($property.Value)) {
            return $property.Value.Trim()
        }
        $found = Find-NestedString -Value $property.Value -Keys $Keys -Depth ($Depth + 1)
        if ($found) { return $found }
    }
    return ''
}

function Get-HookCommand {
    param($Payload)
    $inputObject = Get-PropertyValue $Payload 'input'
    $toolInput = Get-PropertyValue $Payload 'toolInput'
    $toolInputSnake = Get-PropertyValue $Payload 'tool_input'
    $arguments = Get-PropertyValue $Payload 'arguments'
    $paramsObject = Get-PropertyValue $Payload 'params'
    $hookSpecificInput = Get-PropertyValue $Payload 'hookSpecificInput'
    $hookSpecificToolInput = Get-PropertyValue $hookSpecificInput 'toolInput'

    $first = Get-FirstString @(
        (Get-PropertyValue $Payload 'command'),
        (Get-PropertyValue $Payload 'cmd'),
        (Get-PropertyValue $Payload 'script'),
        (Get-PropertyValue $inputObject 'command'),
        (Get-PropertyValue $inputObject 'cmd'),
        (Get-PropertyValue $toolInputSnake 'command'),
        (Get-PropertyValue $toolInput 'command'),
        (Get-PropertyValue $arguments 'command'),
        (Get-PropertyValue $paramsObject 'command'),
        (Get-PropertyValue $hookSpecificToolInput 'command')
    )
    if ($first) { return $first }
    return Find-NestedString -Value $Payload -Keys @('command', 'cmd', 'script')
}

function Get-HookToolName {
    param($Payload)
    $tool = Get-PropertyValue $Payload 'tool'
    $hookSpecificInput = Get-PropertyValue $Payload 'hookSpecificInput'
    return Get-FirstString @(
        (Get-PropertyValue $Payload 'tool_name'),
        (Get-PropertyValue $Payload 'toolName'),
        (Get-PropertyValue $Payload 'name'),
        (Get-PropertyValue $tool 'name'),
        (Get-PropertyValue $tool 'identifier'),
        (Get-PropertyValue $hookSpecificInput 'toolName')
    )
}

function Get-HookPathStrings {
    param($Value, [int] $Depth = 0)
    $results = New-Object System.Collections.Generic.List[string]

    if ($null -eq $Value -or $Depth -gt 6) { return $results }

    if ($Value -is [string]) {
        if ($Value.Length -lt 260) { $results.Add($Value.Trim()) }
        return $results
    }

    if ($Value -is [System.Array]) {
        foreach ($item in $Value) {
            foreach ($path in (Get-HookPathStrings -Value $item -Depth ($Depth + 1))) { $results.Add($path) }
        }
        return $results
    }

    if ($Value -isnot [psobject]) { return $results }

    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -match 'path|file|filename|name' -and $property.Value -is [string] -and -not [string]::IsNullOrWhiteSpace($property.Value)) {
            $results.Add($property.Value.Trim())
        }
        foreach ($path in (Get-HookPathStrings -Value $property.Value -Depth ($Depth + 1))) { $results.Add($path) }
    }

    return $results | Where-Object { $_ -match '[\\/]|\.[A-Za-z0-9]{1,8}$' } | Select-Object -Unique
}