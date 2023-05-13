@description('The name of the environment')
param environment_name string
@description('The location of the resource group')
param location string = resourceGroup().location

@description('The name of the application insights instance')
var appInsightsName = 'appins-${environment_name}'
@description('The name of the container registry')
param tmp string  = replace(environment_name, '-', '')
@description('The name of the log analytics workspace')
var logAnalyticsWorkspaceName = 'logs-${tmp}'
param containerRegistryName string = 'acr${tmp}'
param serviceBusName string = 'sb${tmp}'
param storageAccountName string = 'stg${tmp}'


//Resource log analytics workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    retentionInDays: 30
    features: {
      search: {
        enabled: true
      }
    }
    sku: {
      name: 'PerGB2018'
    }
  }
}

//Resource application insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

//Resource container app environment
resource environment 'Microsoft.App/managedEnvironments@2022-11-01-preview' = {
  name: environment_name
  location: location
  properties: {
    daprAIInstrumentationKey: appInsights.properties.InstrumentationKey
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

//Resource container registry
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: containerRegistryName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    adminUserEnabled: true
  }
}

//Resource service bus
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' =  {
  name: serviceBusName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}


//Resource filetopic in service bus
resource fileTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'filetopic'
  properties: {
    enablePartitioning: true
  }
}

//Resource service bus authrule for listkeys
resource listKeysAuthRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  parent: serviceBus
  name: 'RootManageSharedAccessKey'
  properties: {
    rights: [
      'Listen'
      'Send'
      'Manage'
    ]
  }
}

//module identity
module storageIdentity 'modules/userassigned.bicep' = {
  scope: resourceGroup(resourceGroup().name)
  name: 'storageIdentity'
  params: {
    basename: tmp
    location: location
  }
}

//Resource storage account
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Cool'
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    supportsHttpsTrafficOnly: true
  }
}

//assign idenity with Storage Blob Data Contributor (ba92f5b4-2d11-453d-a403-e96b0029c9fe)
//needed if allowBlobPublicAccess is set to false on the Storage Account
var roleGuid = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
resource role_assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, roleGuid)
  properties: {
    principalId: storageIdentity.outputs.principalId
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', roleGuid)
    principalType: 'ServicePrincipal'
  }
  scope: storageAccount
}

//Attach dapr binding component to storage account
resource storageDaprBindingComponent 'Microsoft.App/managedEnvironments/daprComponents@2022-11-01-preview' = {
  parent: environment
  name: 'files'
  properties: {
    componentType: 'bindings.azure.blobstorage'
    version: 'v1'
    metadata: [
      {
        name: 'storageAccount'
        value: storageAccount.name
      }
      {
        name: 'storageAccountKey'
        value: storageAccount.listKeys().keys[0].value
      }
      {
        name: 'containerName'
        value: 'files'
      }
      {
        name: 'decodeBase64'
        value: 'false'
      }
      {
        name: 'getBlobRetryCount'
        value: '3'
      }
      {
        //needed if allowBlobPublicAccess is set to false on the Storage Account
        name: 'azureClientId'
        value: storageIdentity.outputs.clientId
      }
    ]
    scopes: [
      'daz-api'
    ]
  }
}

var serviceBusEndpoint = '${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey'
var serviceBusConnectionString = listKeys(serviceBusEndpoint, serviceBus.apiVersion).primaryConnectionString
//attach dapr pub/sub component to service bus
resource pubsubDaprComponent 'Microsoft.App/managedEnvironments/daprComponents@2022-11-01-preview' = {
  parent: environment
  name: 'pubsub'
  properties: {
    componentType: 'pubsub.azure.servicebus.topics'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        value: serviceBusConnectionString
      }
    ]
    scopes: [
      'daz-api'
      'daz-subscriber'
    ]
  }
}

//attach dapr pubsub subscription component to service bus
resource pubsubSubscriptionDaprComponent 'Microsoft.App/managedEnvironments/daprComponents@2022-11-01-preview' = {
  parent: environment
  name: 'pubsub-subscription'
  properties: {
    componentType: 'pubsub.azure.servicebus.subscription'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        value: serviceBusConnectionString
      }
      {
        name: 'topic'
        value: 'filetopic'
      }
      {
        name: 'route'
        value: '/fileinfo'
      }
      {
        name: 'pubsubname'
        value : 'pubsub'
      }
    ]
    scopes: [
      'daz-subscriber'
    ]
  }
}

//module publish daz-api
module publishDazApi 'br/public:deployment-scripts/build-acr:2.0.1' = {
  name: 'publishDazApi'
  params: {
    AcrName: containerRegistry.name
    location: location
    gitRepositoryUrl: 'https://github.com/mbn-ms-dk/daz.git'
    dockerfileDirectory: 'SimpleDaprApi'
    imageName: 'daz-api'
    imageTag: 'latest'
  }
}

//module publish daz-api
module publishDazSubscriber 'br/public:deployment-scripts/build-acr:2.0.1' = {
  name: 'publishDazSubscriber'
  params: {
    AcrName: containerRegistry.name
    location: location
    gitRepositoryUrl: 'https://github.com/mbn-ms-dk/daz.git'
    dockerfileDirectory: 'SubConsole'
    imageName: 'daz-subscriber'
    imageTag: 'latest'
  }
}

//Run daz-api
module dazapi 'br/public:app/dapr-containerapp:1.0.1' = {
  name: 'daz-api'
  params: {
    location: location
    containerAppEnvName: environment_name
    containerAppName: 'daz-api'
    containerImage: publishDazApi.outputs.acrImage
    azureContainerRegistry: containerRegistry.name
    enableIngress: true
  }
}

//Run daz-subscriber
module dazsubscriber 'br/public:app/dapr-containerapp:1.0.1' = {
  name: 'daz-subscriber'
  params: {
    location: location
    containerAppEnvName: environment_name
    containerAppName: 'daz-subscriber'
    containerImage: publishDazSubscriber.outputs.acrImage
    azureContainerRegistry: containerRegistry.name
    enableIngress: true
  }
}
  
