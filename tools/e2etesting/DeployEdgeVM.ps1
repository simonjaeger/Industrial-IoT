Param(
    $keyVaultName,
    $resourceGroupName
)

$templateDir = [System.IO.Path]::Combine($PSScriptRoot, "../../deploy/templates") 

# Get IoTHub
$iotHub = Get-AzIotHub -ResourceGroupName $resourceGroupName
$ioTHubConnString = (Get-AzIotHubConnectionString -ResourceGroupName $resourceGroupName -KeyName iothubowner -Name $iotHub.Name).PrimaryConnectionString

# Create MSI for edge and DPS
Write-Host "Creating MSI for edge VM identity and creating DPS"
$edgePrereqsTemplate = [System.IO.Path]::Combine($templateDir, "azuredeploy.edgesimulationprereqs.json")

$templateParameters = @{
    "dpsIotHubHostName" = $iotHub.Properties.HostName
    "dpsIotHubConnectionString" = $ioTHubConnString
    "dpsIotHubLocation" = $iotHub.Location
    "keyVaultName" = $keyVaultName
}

$prereqsDeployment = New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile $edgePrereqsTemplate -TemplateParameterObject $templateParameters
if ($prereqsDeployment.ProvisioningState -ne "Succeeded") {
    Write-Error "Deployment $($prereqsDeployment.ProvisioningState)." -ErrorAction Stop
}

Write-Host "Created MSI $($prereqsDeployment.Parameters.managedIdentityName.Value) with resource id $($prereqsDeployment.Outputs.managedIdentityResourceId.Value)"

# Configure the keyvault
# Allow the MSI to access keyvault
Set-AzKeyVaultAccessPolicy -VaultName $keyVaultName -ObjectId $prereqsDeployment.Outputs.managedIdentityPrincipalId.Value -PermissionsToSecrets get,list,set,delete -PermissionsToKeys get,list,sign,unwrapKey,wrapKey,create -PermissionsToCertificates get,list,update,create,import

# Allow the keyvault to be used in ARM deployments
Set-AzKeyVaultAccessPolicy -VaultName -EnabledForTemplateDeployment

# Deploy edge and simulation virtual machines
$templateParameters = @{
    "keyVaultName" = $keyVaultName
	"managedIdentityResourceId" = $prereqsDeployment.Outputs.managedIdentityResourceId.Value
    "numberOfLinuxGateways" = 1
    "edgePassword" = [System.Web.Security.Membership]::GeneratePassword(15, 5)
}

$simulationTemplate = [System.IO.Path]::Combine($templateDir, "azuredeploy.simulation.json")
Write-Host "Preparing to deploy azuredeploy.simulation.json"

$simulationDeployment = New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile $simulationTemplate -TemplateParameterObject $templateParameters
if ($simulationDeployment.ProvisioningState -ne "Succeeded") {
    Write-Error "Deployment $($simulationDeployment.ProvisioningState)." -ErrorAction Stop
}
Write-Host "Deployed simulation"
