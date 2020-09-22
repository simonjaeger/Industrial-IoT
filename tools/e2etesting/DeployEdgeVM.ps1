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

Write-Host "Created MSI $($msi.Parameters.managedIdentityName.Value) with resource id $($prereqsDeployment.Outputs.managedIdentityResourceId.Value)"

# Deploy edge and simulation vms
$templateParameters = @{
    # secrets pcs-dps-idscope and pcs-dps-connstring are retrieved from this keyvault
    "keyVaultName" = $keyVaultName
	"managedIdentityResourceId" = $prereqsDeployment.Outputs.managedIdentityResourceId.Value
    "numberOfLinuxGateways" = 1
    "edgePassword" = [System.Web.Security.Membership]::GeneratePassword(15, 5)
}

$simulationTemplate = [System.IO.Path]::Combine($templateDir, "azuredeploy.simulation.json")
$simulationDeployment = New-AzResourceGroupDeployment -ResourceGroupName $resourceGroupName -TemplateFile $simulationTemplate -TemplateParameterObject $templateParameters
if ($simulationDeployment.ProvisioningState -ne "Succeeded") {
    Write-Error "Deployment $($simulationDeployment.ProvisioningState)." -ErrorAction Stop
}
Write-Host "Deployed simulation"
