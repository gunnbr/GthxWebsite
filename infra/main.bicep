// Azure resources for the gthx.net read-only API, deployed into their own resource group (rg-gthx-web):
//
// - User-assigned managed identity the function app runs as. It signs in to the bot's Azure SQL database,
//   where its database user only has db_datareader, and to the storage account. No passwords anywhere.
// - Storage account the Functions host needs, which also holds the deployment package (no shared keys)
// - Flex Consumption plan and the function app (.NET 10 isolated, 512 MB instances, scales to zero)
// - Monthly cost budget with email alerts
//
// The static site itself is on Namecheap, not in Azure. CORS lets only gthx.net call the API.

targetScope = 'resourceGroup'

@description('Azure region for all resources. Must support Flex Consumption.')
param location string = resourceGroup().location

@description('Short name used in resource names.')
param appName string = 'gthx-web'

@description('Fully qualified name of the bot\'s Azure SQL server.')
param sqlServerFqdn string

@description('Database name on the Azure SQL server.')
param sqlDatabaseName string

@description('Origins allowed to call the API from a browser.')
param allowedOrigins array

@description('Most instances the API may scale out to. Kept low so traffic spikes can\'t swamp the bot\'s 5-DTU database.')
@minValue(1)
param maximumInstanceCount int = 2

@description('First day of the month the budget starts, yyyy-MM-dd. Must not change after the first deploy.')
param budgetStartDate string

@description('Monthly budget in USD for everything in this resource group.')
param monthlyBudget int = 5

@description('Email address for budget alerts. No budget is created when empty.')
param budgetContactEmail string = ''

var suffix = uniqueString(resourceGroup().id)
var tags = {
  app: 'gthx'
  component: 'website'
}
var deploymentContainerName = 'app-package'

// Built-in role definition IDs
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}'
  location: location
  tags: tags
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stgthxweb${suffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // Everything signs in with the managed identity.
    allowSharedKeyAccess: false
  }

  resource blobService 'blobServices' = {
    name: 'default'

    resource deploymentContainer 'containers' = {
      name: deploymentContainerName
    }
  }
}

// The Functions host keeps its locks and keys in blobs, and the deployment package is a blob, so blob access
// is all the app needs (it has only HTTP triggers).
resource storageBlobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, storageBlobDataOwnerRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${appName}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${appName}-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      cors: {
        allowedOrigins: allowedOrigins
      }
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identity.properties.clientId }
        {
          name: 'ConnectionStrings__GthxDb'
          value: 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
        }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: 512
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
  dependsOn: [
    storageBlobOwner
  ]
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetContactEmail)) {
  name: 'budget-${appName}'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [budgetContactEmail]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetContactEmail]
      }
    }
  }
}

output functionAppName string = functionApp.name
output apiBaseUrl string = 'https://${functionApp.properties.defaultHostName}/api'
output identityName string = identity.name
output identityClientId string = identity.properties.clientId
