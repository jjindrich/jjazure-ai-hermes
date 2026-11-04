targetScope = 'resourceGroup'

@description('Dedicated virtual network created in the sandbox resource group.')
param virtualNetworkName string = 'vnet-jjsandbox-trip'

param virtualNetworkPrefix string = '10.42.0.0/16'

@description('Storage account resource ID for private service endpoints.')
param storageAccountId string

@description('ACA sandbox delegated subnet address range. Must be unused within the VNet.')
param sandboxSubnetPrefix string = '10.42.0.0/23'

@description('Private Endpoint subnet address range. Must be unused within the VNet.')
param privateEndpointSubnetPrefix string = '10.42.2.0/27'

param location string = resourceGroup().location

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: virtualNetworkName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        virtualNetworkPrefix
      ]
    }
    subnets: [
      {
        name: 'aca-sandboxes'
        properties: {
          addressPrefix: sandboxSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
          delegations: [
            {
              name: 'Microsoft.App/environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'storage-private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource sandboxSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: virtualNetwork
  name: 'aca-sandboxes'
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: virtualNetwork
  name: 'storage-private-endpoints'
}

var storageServices = [
  {
    service: 'blob'
    zone: 'privatelink.blob.${environment().suffixes.storage}'
  }
  {
    service: 'queue'
    zone: 'privatelink.queue.${environment().suffixes.storage}'
  }
  {
    service: 'table'
    zone: 'privatelink.table.${environment().suffixes.storage}'
  }
]

resource privateDnsZones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for item in storageServices: {
  name: item.zone
  location: 'global'
}]

resource privateDnsLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (item, index) in storageServices: {
  parent: privateDnsZones[index]
  name: '${virtualNetworkName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    resolutionPolicy: 'Default'
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}]

resource storagePrivateEndpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for item in storageServices: {
  name: 'trip-storage-${item.service}'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'trip-storage-${item.service}'
        properties: {
          privateLinkServiceId: storageAccountId
          groupIds: [
            item.service
          ]
          requestMessage: 'Approved private connectivity for the trip ACA Sandbox.'
        }
      }
    ]
  }
}]

resource storagePrivateEndpointDnsGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (item, index) in storageServices: {
  parent: storagePrivateEndpoints[index]
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: item.service
        properties: {
          privateDnsZoneId: privateDnsZones[index].id
        }
      }
    ]
  }
}]

output sandboxSubnetId string = sandboxSubnet.id
output privateEndpointSubnetId string = privateEndpointSubnet.id
