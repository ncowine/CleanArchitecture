# Creates the exchanges the POCs and the API publish to, so the apps can be started in any order.
#
# Each app declares its OWN exchange when it connects, but only checks that the ones it subscribes to exist, and
# keeps retrying until they do. Without this script, the net472 app waits for the net8 app to have run once (and the
# other way round), and the API's consumer waits for the net472 app. Declaring them up front with the owners' exact
# settings (topic, durable) is harmless: an owner re-declaring its exchange later is a no-op.
#
# Needs the management plugin (http://localhost:15672). Run once per broker:
#   .\samples\MessagingPocs\setup-exchanges.ps1
param(
    [string]$ManagementUrl = 'http://localhost:15672',
    [string]$UserName = 'guest',
    [string]$Password = 'guest'
)

$ErrorActionPreference = 'Stop'
$credential = New-Object PSCredential($UserName, (ConvertTo-SecureString $Password -AsPlainText -Force))
$body = '{"type":"topic","durable":true,"auto_delete":false,"internal":false,"arguments":{}}'

foreach ($exchange in 'CleanArch', 'Poc.Net472', 'Poc.Net8', 'Poc.Net10') {
    Invoke-RestMethod -Method Put -Uri "$ManagementUrl/api/exchanges/%2F/$exchange" -Credential $credential `
        -ContentType 'application/json' -Body $body | Out-Null
    Write-Host "exchange $exchange ready"
}
