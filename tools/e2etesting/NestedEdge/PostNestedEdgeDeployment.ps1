Param(
    [string]
    $ResourceGroupName,
    [Guid]
    $TenantId
)

# Stop execution when an error occurs.
$ErrorActionPreference = "Stop"

if (!$ResourceGroupName) {
    Write-Error "ResourceGroupName not set."
}

## Login if required

$context = Get-AzContext

if (!$context) {
    Write-Host "Logging in..."
    Login-AzAccount -Tenant $TenantId
    $context = Get-AzContext
}

## Check if resource group exists

$resourceGroup = Get-AzResourceGroup -Name $resourceGroupName

if (!$resourceGroup) {
    Write-Error "Could not find Resource Group '$($ResourceGroupName)'."
}


## Check if IoT Hub exists
$iotHub = Get-AzIotHub -ResourceGroupName $ResourceGroupName

if ($iotHub.Count -ne 1) {
    Write-Error "IotHub could not be automatically selected in Resource Group '$($ResourceGroupName)'."    
}

Write-Host "IoT Hub Name: $($iotHub.Name)"

$deviceName = "L3-1-edge"
Add-AzIotHubDevice -ResourceGroupName $ResourceGroupName -IotHubName $iotHub.Name -DeviceId $deviceName -EdgeEnabled

$deviceL4 = "L4-1-edge"
Add-AzIotHubDevice -ResourceGroupName $ResourceGroupName -IotHubName $iotHub.Name -DeviceId $deviceL4 -EdgeEnabled

$deviceL5 = "L5-1-edge"
Add-AzIotHubDevice -ResourceGroupName $ResourceGroupName -IotHubName $iotHub.Name -DeviceId $deviceL5 -EdgeEnabled

$keyVault = "e2etestingkeyVault" + $resourceGroup.Tags["TestingResourcesSuffix"]
Write-Host "Key Vault Name: $($keyVault)"

$edgeIdentity = Get-AzIotHubDevice -ResourceGroupName $ResourceGroupName -IotHubName $iotHub.Name -DeviceId $deviceName -ErrorAction SilentlyContinue

Write-Host "Adding/Updating KeyVault-Secret 'iot-edge-device-id' with value '$($edgeIdentity.Id)'..."
Set-AzKeyVaultSecret -VaultName $keyVault -Name 'iot-edge-device-id' -SecretValue (ConvertTo-SecureString $edgeIdentity.Id -AsPlainText -Force) | Out-Null


Write-Host "Updating 'os' and '__type__'-Tags in Device Twin..."
Update-AzIotHubDeviceTwin -ResourceGroupName $ResourceGroupName -IotHubName $iotHub.Name -DeviceId $edgeIdentity.Id -Tag @{ "os" = "Linux"; "__type__" = "iiotedge"; "unmanaged" = "true" } | Out-Null

$edgeVmUsername = "jbadmin"
Write-Host "Adding/Updating KeVault-Secret 'iot-edge-vm-username' with value '$($edgeVmUsername)'..."
Set-AzKeyVaultSecret -VaultName $keyVault -Name 'iot-edge-vm-username' -SecretValue (ConvertTo-SecureString $edgeVmUsername -AsPlainText -Force) | Out-Null


$fqdn = (Get-AzPublicIpAddress -ResourceGroupName ($ResourceGroupName + "-RG-jumpbox")).DnsSettings.Fqdn
Write-Host "Adding/Updating KeyVault-Secret 'iot-edge-device-dnsname' with value '$($fqdn)'..."
Set-AzKeyVaultSecret -VaultName $keyVault -Name 'iot-edge-device-dnsname' -SecretValue (ConvertTo-SecureString $fqdn -AsPlainText -Force) | Out-Null

Write-Host "Deployment finished."

