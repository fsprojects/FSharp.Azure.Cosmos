---
title: How To handle sub-status codes
category: How To Guides
categoryindex: 2
index: 3
---

# How To handle sub-status codes

A Cosmos DB response carries, besides its HTTP status code, a sub-status code in the `x-ms-substatus` header.
It tells apart failures that share a status code. For example, a point read answers `404 Not Found` both
when the item does not exist and when its container does not exist; only the sub-status shows which one it is.

## Reading the sub-status

The SDK exposes the sub-status of a failed typed operation as `CosmosException.SubStatusCode`, but not on the
`ResponseMessage` returned by the stream APIs (`ReadItemStreamAsync`, `CreateItemStreamAsync`, …).
FSharp.Azure.Cosmos adds it there:

``` F#
open System.Net
open Microsoft.Azure.Cosmos
open FSharp.Azure.Cosmos

let itemExistsAsync (container : Container) (id : string) (partitionKey : PartitionKey) = task {
    use! response = container.ReadItemStreamAsync (id, partitionKey)
    match response.StatusCode, response.SubStatusCode with
    // The item does not exist
    | HttpStatusCode.NotFound, SubStatusCodes.Unknown -> return false
    // Anything else, including a missing database or container (404 with OwnerResourceNotFound),
    // is either success or a failure that must not be reported as a missing item
    | _ ->
        response.EnsureSuccessStatusCode () |> ignore
        return true
}
```

`response.SubStatusCode` is an F# extension property. The same value is returned by the
`ResponseMessage.getSubStatusCode` function, which C# calls as `ResponseMessageModule.GetSubStatusCode`.
It returns `SubStatusCodes.Unknown` (0) when the response has no sub-status.

The constants in `SubStatusCodes` are literals, so they can be used in F# patterns as above, and they compile to
`const` fields that C# can use in `switch`:

``` C#
using System.Net;
using FSharp.Azure.Cosmos;

switch ((response.StatusCode, ResponseMessageModule.GetSubStatusCode(response)))
{
    case (HttpStatusCode.NotFound, SubStatusCodes.Unknown):
        // The item does not exist
        break;
    case (HttpStatusCode.NotFound, SubStatusCodes.OwnerResourceNotFound):
        // The database or container does not exist
        break;
}
```

The same constants work with `CosmosException.SubStatusCode` from the typed APIs
(`Person` is the record from [Getting Started](../Tutorials/Getting_Started.html)):

``` F#
let tryCreateAsync (container : Container) (item : Person) = task {
    try
        let! _ = container.CreateItemAsync (item, PartitionKey item.TenantId)
        return true
    with
    | :? CosmosException as ex when
        ex.StatusCode = HttpStatusCode.Forbidden
        && ex.SubStatusCode = SubStatusCodes.PartitionKeyQuotaExceeded
        ->
        // The logical partition reached its maximum size
        return false
}
```

## Always match on the status code too

A sub-status only has a meaning together with the HTTP status code. Some numbers are reused for different
statuses:

| Sub-status | With status | Constant |
| --- | --- | --- |
| 1002 | 404 Not Found | `ReadSessionNotAvailable` |
| 1002 | 410 Gone | `PartitionKeyRangeGone` |
| 1007 | 410 Gone | `CompletingSplit` |
| 1007 | 503 Service Unavailable | `InsufficientBindablePartitions` |
| 1008 | 403 Forbidden | `DatabaseAccountNotFound` |
| 1008 | 410 Gone | `CompletingPartitionMigration` |

That is why `SubStatusCodes` is a set of constants rather than an enumeration, and why the examples above match
on the `(status, sub-status)` pair.

## Known sub-status codes

`SubStatusCodes` contains the codes documented on Microsoft Learn. Where Learn names a code without its HTTP
status, the status comes from the official Python, Java and Rust SDK sources, and for the customer-managed key
codes from the data plane scenario of the
[customer-managed key troubleshooting guide](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-cmk).

| Status | Sub-status | Constant | Meaning |
| --- | --- | --- | --- |
| any | 0 | `Unknown` | No sub-status |
| 400 Bad Request | 1001 | `PartitionKeyMismatch` | The partition key does not match the item or the container's partition key definition |
| 400 Bad Request | 1004 | `CrossPartitionQueryNotServable` | The cross-partition query cannot be served |
| 400 Bad Request | 1024 | `CollectionRidMismatch` | The container resource id does not match the one the client cached, for example after the container was re-created with the same name |
| 403 Forbidden | 3 | `WriteForbidden` | A write was sent to a region that no longer accepts writes during a manual failover |
| 403 Forbidden | 1005 | `ProvisionLimitReached` | The provisioning limit is reached |
| 403 Forbidden | 1008 | `DatabaseAccountNotFound` | The database account is not found |
| 403 Forbidden | 1009 | `RedundantCollectionPut` | The container definition in the request is the same as the existing one |
| 403 Forbidden | 1010 | `SharedThroughputDatabaseQuotaExceeded` | The quota of containers in a shared throughput database is exceeded |
| 403 Forbidden | 1011 | `SharedThroughputOfferGrowNotNeeded` | The shared throughput offer does not need to grow |
| 403 Forbidden | 1014 | `PartitionKeyQuotaExceeded` | The logical partition reached its maximum size |
| 403 Forbidden | 4000 | `AadTokenAcquisitionFailed` | Customer-managed keys: Azure Cosmos DB cannot get the Microsoft Entra ID access token for the Key Vault |
| 403 Forbidden | 4001 | `AadServiceUnavailable` | Customer-managed keys: the Microsoft Entra ID service is unavailable |
| 403 Forbidden | 4002 | `KeyVaultAccessDenied` | Customer-managed keys: the Key Vault does not grant Azure Cosmos DB access, or the key is disabled |
| 403 Forbidden | 4003 | `KeyVaultKeyNotFound` | Customer-managed keys: the key is not found in the Key Vault |
| 403 Forbidden | 4004 | `KeyVaultServiceUnavailable` | Customer-managed keys: the Key Vault service is unavailable |
| 403 Forbidden | 4005 | `KeyVaultWrapUnwrapFailure` | Customer-managed keys: the Key Vault cannot wrap or unwrap the key |
| 403 Forbidden | 4006 | `InvalidKeyVaultKeyUrl` | Customer-managed keys: the Key Vault key URL is invalid, for example it includes the key version |
| 403 Forbidden | 4007 | `InvalidInputBytes` | Customer-managed keys: internal server error, the input bytes are not in the base64 format |
| 403 Forbidden | 4008 | `KeyVaultInternalServerError` | Customer-managed keys: the Key Vault returned an internal service error |
| 403 Forbidden | 4009 | `KeyVaultDnsNotResolved` | Customer-managed keys: the Key Vault DNS name cannot be resolved |
| 403 Forbidden | 5300 | `AadRequestNotAuthorized` | The request cannot be authorized by a Microsoft Entra ID token in the data plane |
| 404 Not Found | 1002 | `ReadSessionNotAvailable` | The read session is not available for the session token |
| 404 Not Found | 1003 | `OwnerResourceNotFound` | The owner resource, a database or container, does not exist |
| 404 Not Found | 1013 | `ContainerCreateInProgress` | The container create operation is still in progress; retry the read until it succeeds |
| 404 / 400 | 10004 | `ThroughputOfferNotFound` | Generated by the client SDK, not the service, when the container has no throughput offer: the Python SDK pairs it with 404, the Java SDK with 400 |
| 409 Conflict | 1006 | `ConflictWithControlPlane` | The operation conflicts with a control plane operation |
| 410 Gone | 1000 | `NameCacheIsStale` | The client's name cache is stale |
| 410 Gone | 1002 | `PartitionKeyRangeGone` | The partition key range is gone, for example after a split |
| 410 Gone | 1007 | `CompletingSplit` | A partition split is completing |
| 410 Gone | 1008 | `CompletingPartitionMigration` | A partition migration is completing |
| 429 Too Many Requests | 3212 | `ThroughputBucketMaxThroughputExceeded` | The throughput bucket exceeded its configured maximum throughput |
| 429 Too Many Requests | 3213 | `ThroughputBucketConfigurationChangeThrottled` | The throughput bucket configuration was changed more than once in 10 minutes |
| 503 Service Unavailable | 1007 | `InsufficientBindablePartitions` | There are not enough bindable partitions |
| 503 Service Unavailable | 20001 | `ClientConnectivityFailure` | The client failed to connect and all retries failed |
| 503 Service Unavailable | 20002 | `ClientTimeout` | The client timed out and all retries failed |
| 503 Service Unavailable | 20003 | `ClientOperatingSystemIoError` | An operating system I/O error occurred on the client |
| 503 Service Unavailable | 20004 | `ClientCpuOverload` | The client machine's CPU is overloaded |
| 503 Service Unavailable | 20005 | `ClientThreadStarvation` | The client machine's thread pool is starved |
| 503 Service Unavailable | 20006 | `ConnectionClosed` | The connection was interrupted or terminated unexpectedly |
| 503 Service Unavailable | 21001 and greater | `TransientServiceConditionStart` | A transient service condition |
| 503 Service Unavailable | 21015 | `RoutingMapSnapshotInconsistent` | The routing map snapshot is inconsistent |

The service can return sub-status codes that are not in this list, so always keep a fallback case.

## Emulators do not always match the service

The emulators do not reproduce every sub-status of the service. For example, a point read with a prefix of a
hierarchical partition key is rejected with `400 Bad Request` and sub-status 1001 (`PartitionKeyMismatch`) by the
Windows emulator, but with sub-status 0 by the Linux vNext emulator. Code that has to behave the same against
both should not depend on a sub-status where the status code alone is enough, and tests that assert on a
sub-status should run against the backend they are meant to cover.

## See also

- [HTTP status codes for Azure Cosmos DB](https://learn.microsoft.com/rest/api/cosmos-db/http-status-codes-for-cosmosdb)
- [Troubleshoot bad request exceptions](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-bad-request)
- [Troubleshoot forbidden exceptions](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-forbidden)
- [Troubleshoot not found exceptions](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-not-found)
- [Troubleshoot service unavailable exceptions](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-service-unavailable)
