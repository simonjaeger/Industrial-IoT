Param(
    [string]
    $ResourceGroupName,
    [Guid]
    $TenantId
)

Write-Host (Get-Location).Path

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

## Determine suffix for testing resources

$testSuffix = $resourceGroup.Tags["TestingResourcesSuffix"]

if (!$testSuffix) {
    $testSuffix = Get-Random -Minimum 10000 -Maximum 99999

    $tags = $resourceGroup.Tags
    $tags+= @{"TestingResourcesSuffix" = $testSuffix}
    Set-AzResourceGroup -Name $resourceGroup.ResourceGroupName -Tag $tags | Out-Null
    $resourceGroup = Get-AzResourceGroup -Name $resourceGroup.ResourceGroupName
}

Write-Host "Using suffix for testing resources: $($testSuffix)"

## Check if IoT Hub exists
$iotHub = Get-AzIotHub -ResourceGroupName $ResourceGroupName

if ($iotHub.Count -ne 1) {
    Write-Error "IotHub could not be automatically selected in Resource Group '$($ResourceGroupName)'."    
}

Write-Host "IoT Hub Name: $($iotHub.Name)"
Write-Host "##vso[task.setvariable variable=iothub;isSecret=false;isOutput=true;]$($iotHub.Name)"

## Create ACR
$acrName = "ACR" + $testSuffix
$registry = New-AzContainerRegistry -ResourceGroupName $ResourceGroupName -Name $acrName -EnableAdminUser -Sku Basic
$creds = Get-AzContainerRegistryCredential -Registry $registry

## Update ACR.env file
$acrFile = "./NestedEdge/ACR.env"
$arcEnvOriginal =  Get-Content $acrFile
$acrEnv = $arcEnvOriginal
$acrEnv = $acrEnv -replace 'YOUR_ACR_ADDRESS', ($creds.Username + ".azurecr.io")
$acrEnv = $acrEnv -replace 'YOUR_ACR_USERNAME', $creds.Username
$acrEnv = $acrEnv -replace 'YOUR_ACR_PASSWORD', $creds.Password
$acrEnv | Out-File $acrFile