Param(
    [string]
    $ResourceGroupName,
    [Guid]
    $TenantId,
    [String]
    $Region = "EastUS",
    [String]
    $ServicePrincipalId
)

# Stop execution when an error occurs.
$ErrorActionPreference = "Stop"

if (!$ResourceGroupName) {
    Write-Error "ResourceGroupName not set."
}

if (!$Region) {
    Write-Error "Region not set."
}

if (!$ServicePrincipalId) {
    Write-Warning "ServicePrincipalId not set, cannot update permissions."
}

## Login if required

$context = Get-AzContext

if (!$context) {
    Write-Host "Logging in..."
    Login-AzAccount -Tenant $TenantId
    $context = Get-AzContext
}

## Check if resource group exists

$resourceGroup = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue

if (!$resourceGroup) {
    Write-Host "Creating Resource Group $($ResourceGroupName) in $($Region)..."
    $resourceGroup = New-AzResourceGroup -Name $ResourceGroupName -Location $Region
}

Write-Host "Resource Group: $($resourceGroup.ResourceGroupName)"

## Determine suffix for testing resources

if (!$resourceGroup.Tags) {
    $resourceGroup.Tags = @{}
}

$testSuffix = $resourceGroup.Tags["TestingResourcesSuffix"]

if (!$testSuffix) {
    $testSuffix = Get-Random -Minimum 10000 -Maximum 99999

    $tags = $resourceGroup.Tags
    $tags+= @{"TestingResourcesSuffix" = $testSuffix}
    Set-AzResourceGroup -Name $resourceGroup.ResourceGroupName -Tag $tags | Out-Null
    $resourceGroup = Get-AzResourceGroup -Name $resourceGroup.ResourceGroupName
}

Write-Host "Resources Suffix: $($testSuffix)"

$iotHubName = "e2etesting-iotHub-$($testSuffix)"
$keyVaultName = "e2etestingkeyVault$($testSuffix)"

Write-Host "IoT Hub: $($iotHubName)"
Write-Host "Key Vault: $($keyVaultName)"

## Ensure IoT Hub
$iotHub = Get-AzIotHub -ResourceGroupName $ResourceGroupName -Name $iotHubName -ErrorAction SilentlyContinue

if (!$iotHub) {
    Write-Host "Creating IoT Hub $($iotHubName)..."
    $iotHub = New-AzIotHub -ResourceGroupName $ResourceGroupName -Name $iotHubName -SkuName S1 -Units 1 -Location $resourceGroup.Location
}

## Ensure KeyVault

$keyVault = Get-AzKeyVault -ResourceGroupName $ResourceGroupName -VaultName $keyVaultName -ErrorAction SilentlyContinue

if (!$keyVault) {
    Write-Host "Creating Key Vault $($keyVaultName)"
    $keyVault = New-AzKeyVault -ResourceGroupName $ResourceGroupName -VaultName $keyVaultName -Location $resourceGroup.Location
}

if ($ServicePrincipalId) {
    Write-Host "Setting Key Vault Permissions for Service Principal $($ServicePrincipalId)..."
    Set-AzKeyVaultAccessPolicy -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName -ServicePrincipalName $ServicePrincipalId -PermissionsToSecrets get,list,set | Out-Null
}

$connectionString = Get-AzIotHubConnectionString $ResourceGroupName -Name $iothub.Name -KeyName "iothubowner"

Write-Host "Adding/Updating KeyVault-Secret 'PCS-IOTHUB-CONNSTRING' with value '***'..."
Set-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name 'PCS-IOTHUB-CONNSTRING' -SecretValue (ConvertTo-SecureString $connectionString.PrimaryConnectionString -AsPlainText -Force) | Out-Null

Write-Host "Adding/Updating KeyVault-Secret 'PCS_CONTAINER_REGISTRY_SERVER' with value '***'..."
$containerRegistryServer = $env:ContainerRegistryServer
Set-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name 'PCS_CONTAINER_REGISTRY_SERVER' -SecretValue (ConvertTo-SecureString $containerRegistryServer -AsPlainText -Force) | Out-Null

Write-Host "Adding/Updating KeyVault-Secret 'PCS_CONTAINER_REGISTRY_USER' with value '***'..."
$containerRegistryUser = $env:ContainerRegistryUsername
Set-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name 'PCS_CONTAINER_REGISTRY_USER' -SecretValue (ConvertTo-SecureString $containerRegistryUser -AsPlainText -Force) | Out-Null

Write-Host "Adding/Updating KeyVault-Secret 'PCS_CONTAINER_REGISTRY_PASSWORD' with value '***'..."
$containerRegistryPassword = $env:ContainerRegistryUsername
Set-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name 'PCS_CONTAINER_REGISTRY_PASSWORD' -SecretValue (ConvertTo-SecureString $ContainerRegistryPassword -AsPlainText -Force) | Out-Null

Write-Host "Adding/Updating KeyVault-Secret 'PCS_IMAGES_NAMESPACE' with value '***'..."
$imageNamespace = $env:ImageNamespace
Set-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name 'PCS_IMAGES_NAMESPACE' -SecretValue (ConvertTo-SecureString $imageNamespace -AsPlainText -Force) | Out-Null

Write-Host "Adding/Updating KeyVault-Secret 'PCS_IMAGES_TAG' with value '***'..."
$imageTags = $env:PlatformVersion
Set-AzKeyVaultSecret -VaultName $keyVault.VaultName -Name 'PCS_IMAGES_TAG' -SecretValue (ConvertTo-SecureString $imageTags -AsPlainText -Force) | Out-Null

Write-Host "Deployment finished."