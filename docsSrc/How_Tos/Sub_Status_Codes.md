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
| 1012 | 403 Forbidden | `RedundantDatabasePut` |
| 1012 | 503 Service Unavailable | `ComputeFederationNotFound` |
| 1013 | 400 Bad Request | `PartitionKeyDefinitionNotSpecified` |
| 1013 | 404 Not Found | `ContainerCreateInProgress` |
| 1024 | 400 Bad Request | `CollectionRidMismatch` |
| 1024 | 410 Gone | `ArchivalPartitionNotPresent` |
| 1031 | 403 Forbidden | `SystemPartitionKeyNotAllowed` |
| 1031 | 404 Not Found | `PartitionMigratingCollectionDeleted` |
| 1034 | 403 Forbidden | `ResourceSoftDeleted` |
| 1034 | 404 Not Found | `PartitionMigrationSourcePartitionDeletedInMaster` |
| 2001 | 204 No Content | `MissedTargetLsn` |
| 2001 | 412 Precondition Failed | `SplitIsDisabled` |
| 2002 | 204 No Content | `MissedTargetLsnOver100` |
| 2002 | 412 Precondition Failed | `CollectionsInPartitionGotUpdated` |
| 2003 | 204 No Content | `MissedTargetLsnOver1000` |
| 2003 | 412 Precondition Failed | `CannotAcquirePartitionKeyRangesLock` |
| 2004 | 204 No Content | `MissedTargetLsnOver10000` |
| 2004 | 412 Precondition Failed | `ResourceNotFound` |
| 2011 | 204 No Content | `MissedTargetGlobalCommittedLsn` |
| 2011 | 412 Precondition Failed | `StorageSplitConflictingWithNWayThroughputSplit` |
| 2012 | 204 No Content | `MissedTargetGlobalCommittedLsnOver100` |
| 2012 | 412 Precondition Failed | `MergeIsDisabled` |
| 3207 | 409 Conflict | `ConfigurationNameAlreadyExists` |
| 3207 | 429 Too Many Requests | `PrepareTimeLimitExceeded` |

That is why `SubStatusCodes` is a set of constants rather than an enumeration, and why the examples above match
on the `(status, sub-status)` pair.

## Known sub-status codes

`SubStatusCodes` contains the codes from Microsoft Learn (the REST API HTTP status codes page, the troubleshooting
guides and the throughput buckets FAQ), from the `SubStatusCodes` enumeration of the .NET SDK and from the
sub-status tables of the official Python, Java and Rust SDKs. The names follow the .NET SDK.

The HTTP status of a code is the one stated by Learn or by the SDK sources; for the customer-managed key codes it
comes from the data plane scenario of the
[customer-managed key troubleshooting guide](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-cmk).
A status marked *inferred* is not stated by any of these sources and is derived from the family of the code, so
treat it as a hint rather than a guarantee. Codes generated only by the Java or Rust SDK clients are not included,
because the .NET SDK never returns them.

| Status | Sub-status | Constant | Meaning |
| --- | --- | --- | --- |
| any | 0 | `Unknown` | No sub-status |
| 204 No Content | 2001 | `MissedTargetLsn` | The replica has not reached the target LSN of a head request |
| 204 No Content | 2002 | `MissedTargetLsnOver100` | The replica is more than 100 LSNs behind the target of a head request |
| 204 No Content | 2003 | `MissedTargetLsnOver1000` | The replica is more than 1000 LSNs behind the target of a head request |
| 204 No Content | 2004 | `MissedTargetLsnOver10000` | The replica is more than 10000 LSNs behind the target of a head request |
| 204 No Content | 2011 | `MissedTargetGlobalCommittedLsn` | The replica has not reached the target global committed LSN of a head request |
| 204 No Content | 2012 | `MissedTargetGlobalCommittedLsnOver100` | The replica is more than 100 global committed LSNs behind the target of a head request |
| 204 No Content | 2013 | `MissedTargetGlobalCommittedLsnOver1000` | The replica is more than 1000 global committed LSNs behind the target of a head request |
| 204 No Content | 2014 | `MissedTargetGlobalCommittedLsnOver10000` | The replica is more than 10000 global committed LSNs behind the target of a head request |
| 400 Bad Request | 1001 | `PartitionKeyMismatch` | The partition key does not match the item or the container's partition key definition |
| 400 Bad Request | 1004 | `CrossPartitionQueryNotServable` | The cross-partition query cannot be served |
| 400 Bad Request | 1013 | `PartitionKeyDefinitionNotSpecified` | The partition key definition is not specified |
| 400 Bad Request | 1016 | `SchemaOwnerIdMismatch` | The schema owner id does not match |
| 400 Bad Request | 1017 | `SchemaHashOrIdMismatch` | The schema hash or id does not match |
| 400 Bad Request | 1018 | `PartitionKeyDefinitionMissingForAutopilot` | The partition key definition is missing for an autoscale (autopilot) container |
| 400 Bad Request | 1024 | `CollectionRidMismatch` | The container resource id does not match the one the client cached, for example after the container was re-created with the same name |
| 400 Bad Request | 1101 | `HttpListenerException` | The HTTP listener failed |
| 400 Bad Request | 1102 | `TransactionAlreadyActive` | A transaction is already active |
| 400 Bad Request | 1103 | `InvalidTransactionId` | The transaction id is invalid |
| 400 Bad Request | 1104 | `CrossCollectionTransactionNotSupported` | A transaction across containers is not supported |
| 400 Bad Request | 1105 | `InvalidTopologyChangeRequest` | The topology change request is invalid |
| 400 Bad Request | 3205 | `AnotherOfferReplaceOperationIsInProgress` | Another throughput replace operation is in progress |
| 400 Bad Request (*inferred*) | 13000 | `ThinProxyMultipleAccountsNotAllowed` | The thin client proxy does not allow multiple accounts on the same connection |
| 400 Bad Request (*inferred*) | 20007 | `MalformedContinuationToken` | Generated by the .NET SDK: the continuation token is malformed |
| 400 Bad Request | 65535 | `ScriptCompileError` | A stored procedure, trigger or user-defined function failed to compile |
| 401 Unauthorized (*inferred*) | 5000 | `MissingAuthHeader` | The authorization header is missing |
| 401 Unauthorized (*inferred*) | 5001 | `InvalidAuthHeaderFormat` | The authorization header has an invalid format |
| 401 Unauthorized (*inferred*) | 5002 | `AadAuthDisabled` | Microsoft Entra ID authentication is disabled for the account |
| 401 Unauthorized (*inferred*) | 5003 | `AadTokenInvalidFormat` | The Microsoft Entra ID token has an invalid format |
| 401 Unauthorized (*inferred*) | 5004 | `AadTokenInvalidSignature` | The Microsoft Entra ID token has an invalid signature |
| 401 Unauthorized (*inferred*) | 5005 | `AadTokenNotYetValid` | The Microsoft Entra ID token is not valid yet |
| 401 Unauthorized (*inferred*) | 5006 | `AadTokenExpired` | The Microsoft Entra ID token has expired |
| 401 Unauthorized (*inferred*) | 5007 | `AadTokenInvalidIssuer` | The Microsoft Entra ID token has an invalid issuer |
| 401 Unauthorized (*inferred*) | 5008 | `AadTokenInvalidAudience` | The Microsoft Entra ID token has an invalid audience |
| 401 Unauthorized (*inferred*) | 5009 | `AadTokenInvalidScope` | The Microsoft Entra ID token has an invalid scope |
| 401 Unauthorized (*inferred*) | 5010 | `FailedToGetAadToken` | The Microsoft Entra ID token could not be obtained |
| 401 Unauthorized (*inferred*) | 5011 | `AadTokenMissingObjectIdentifier` | The Microsoft Entra ID token has no object identifier |
| 401 Unauthorized (*inferred*) | 5012 | `SasTokenAuthDisabled` | SAS token authentication is disabled for the account |
| 401 Unauthorized (*inferred*) | 5013 | `AadTokenRevoked` | The Microsoft Entra ID token was revoked |
| 401 Unauthorized (*inferred*) | 5200 | `AadTokenInvalidSigningKey` | The Microsoft Entra ID token has an invalid signing key |
| 401 Unauthorized (*inferred*) | 5201 | `AadTokenGroupExpansionError` | The groups of the Microsoft Entra ID token could not be expanded |
| 401 Unauthorized (*inferred*) | 5202 | `LocalAuthDisabled` | Key-based (local) authentication is disabled for the account |
| 401 Unauthorized (*inferred*) | 6053 | `FabricTokenValidationFailed` | Microsoft Fabric: the token could not be validated |
| 401 Unauthorized (*inferred*) | 6054 | `InvalidFabricAppId` | Microsoft Fabric: the application id is invalid |
| 401 Unauthorized (*inferred*) | 6055 | `InvalidFabricTenantId` | Microsoft Fabric: the tenant id is invalid |
| 401 Unauthorized (*inferred*) | 6056 | `InvalidFabricArtifactId` | Microsoft Fabric: the artifact id is invalid |
| 401 Unauthorized (*inferred*) | 13008 | `ThinProxyGenerated401` | Generated by the thin client proxy |
| 403 Forbidden | 3 | `WriteForbidden` | A write was sent to a region that no longer accepts writes during a manual failover |
| 403 Forbidden | 1005 | `ProvisionLimitReached` | The provisioning limit is reached |
| 403 Forbidden | 1008 | `DatabaseAccountNotFound` | The database account is not found |
| 403 Forbidden | 1009 | `RedundantCollectionPut` | The container definition in the request is the same as the existing one |
| 403 Forbidden | 1010 | `SharedThroughputDatabaseQuotaExceeded` | The quota of containers in a shared throughput database is exceeded |
| 403 Forbidden | 1011 | `SharedThroughputOfferGrowNotNeeded` | The shared throughput offer does not need to grow |
| 403 Forbidden | 1012 | `RedundantDatabasePut` | The database definition in the request is the same as the existing one |
| 403 Forbidden | 1014 | `PartitionKeyQuotaExceeded` | The logical partition reached its maximum size |
| 403 Forbidden | 1015 | `OfferReplaceDisabledAutoscaleOffer` | Replacing the throughput of an autoscale offer is disabled |
| 403 Forbidden | 1019 | `SharedThroughputDatabaseCollectionCountExceeded` | The number of containers in a shared throughput database is exceeded |
| 403 Forbidden | 1020 | `SharedThroughputDatabaseCountExceeded` | The number of shared throughput databases is exceeded |
| 403 Forbidden | 1021 | `ComputeInternalError` | An internal compute error occurred |
| 403 Forbidden | 1026 | `ClientIdMismatch` | The client id does not match |
| 403 Forbidden | 1027 | `UniqueIndexReindexInProgress` | A unique index re-indexing is in progress |
| 403 Forbidden | 1028 | `ThroughputCapQuotaExceeded` | The account throughput cap is exceeded |
| 403 Forbidden | 1029 | `InvalidThroughputCapValue` | The throughput cap value is invalid |
| 403 Forbidden | 1031 | `SystemPartitionKeyNotAllowed` | A system partition key is not allowed |
| 403 Forbidden | 1032 | `PartitionKeyDeleteRequestLimitExceeded` | The limit of delete-by-partition-key requests is exceeded |
| 403 Forbidden | 1033 | `LeakedPartition` | The partition is leaked |
| 403 Forbidden | 1034 | `ResourceSoftDeleted` | The resource is soft-deleted |
| 403 Forbidden | 1110 | `PatchConditionNotMet` | The condition of a conditional patch is not met |
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
| 403 Forbidden (*inferred*) | 4010 | `InvalidKeyVaultCertUrl` | Customer-managed keys: the Key Vault certificate URL is invalid |
| 403 Forbidden (*inferred*) | 4011 | `InvalidKeyVaultKeyAndCertUrl` | Customer-managed keys: the Key Vault key and certificate URLs are invalid |
| 403 Forbidden (*inferred*) | 4012 | `CustomerKeyRotated` | Customer-managed keys: the customer key was rotated |
| 403 Forbidden (*inferred*) | 4013 | `MissingRequestParameter` | Customer-managed keys: a request parameter is missing |
| 403 Forbidden (*inferred*) | 4014 | `InvalidKeyVaultSecretUrl` | Customer-managed keys: the Key Vault secret URL is invalid |
| 403 Forbidden (*inferred*) | 4015 | `UndefinedDefaultIdentity` | Customer-managed keys: the account has no default identity |
| 403 Forbidden (*inferred*) | 4016 | `KeyVaultOutboundDeniedByNsp` | Customer-managed keys: a network security perimeter denies outbound access to the Key Vault |
| 403 Forbidden (*inferred*) | 4017 | `KeyVaultNotFound` | Customer-managed keys: the Key Vault is not found |
| 403 Forbidden (*inferred*) | 4018 | `KeyDisabledOrExpired` | Customer-managed keys: the key is disabled or expired |
| 403 Forbidden (*inferred*) | 4019 | `MasterServiceUnavailable` | Customer-managed keys: the master service is unavailable |
| 403 Forbidden | 5300 | `AadRequestNotAuthorized` | The request cannot be authorized by a Microsoft Entra ID token in the data plane |
| 403 Forbidden (*inferred*) | 5301 | `RbacUnauthorizedMetadataRequest` | Role-based access control does not authorize the metadata request |
| 403 Forbidden (*inferred*) | 5302 | `RbacUnauthorizedNameBasedDataRequest` | Role-based access control does not authorize the name-based data request |
| 403 Forbidden (*inferred*) | 5303 | `RbacUnauthorizedRidBasedDataRequest` | Role-based access control does not authorize the resource-id-based data request |
| 403 Forbidden (*inferred*) | 5304 | `RbacRidCannotBeResolved` | Role-based access control cannot resolve the resource id |
| 403 Forbidden (*inferred*) | 5305 | `RbacMissingUserId` | Role-based access control has no user id for the request |
| 403 Forbidden (*inferred*) | 5306 | `RbacMissingAction` | Role-based access control has no action for the request |
| 403 Forbidden (*inferred*) | 5307 | `NspInboundDenied` | A network security perimeter denies the inbound request |
| 403 Forbidden (*inferred*) | 5400 | `RbacRequestWasNotAuthorized` | Role-based access control did not authorize the request |
| 403 Forbidden (*inferred*) | 6050 | `InsufficientFabricPermissions` | Microsoft Fabric: the permissions are insufficient |
| 403 Forbidden (*inferred*) | 6051 | `FabricAuthorizationFailed` | Microsoft Fabric: authorization failed |
| 403 Forbidden (*inferred*) | 6052 | `FabricOperationUnsupported` | Microsoft Fabric: the operation is not supported |
| 403 Forbidden (*inferred*) | 13001 | `ThinProxyPublicEndpointDisabled` | The public endpoint of the thin client proxy is disabled |
| 404 Not Found | 1002 | `ReadSessionNotAvailable` | The read session is not available for the session token |
| 404 Not Found | 1003 | `OwnerResourceNotFound` | The owner resource, a database or container, does not exist |
| 404 Not Found | 1013 | `ContainerCreateInProgress` | The container create operation is still in progress; retry the read until it succeeds |
| 404 Not Found | 1023 | `StoreNotReady` | The store is not ready |
| 404 Not Found | 1030 | `AuthTokenNotFoundInCache` | The authorization token is not found in the cache |
| 404 Not Found | 1031 | `PartitionMigratingCollectionDeleted` | The container of a migrating partition was deleted |
| 404 Not Found | 1034 | `PartitionMigrationSourcePartitionDeletedInMaster` | The source partition of a partition migration was deleted in the master partition |
| 404 Not Found | 1035 | `PartitionMigrationSharedThroughputDatabasePartitionNotFound` | The partition of a shared throughput database is not found during a partition migration |
| 404 Not Found | 1036 | `PartitionMigrationPartitionResourceNotFound` | The partition resource is not found during a partition migration |
| 404 Not Found | 1037 | `PartitionMigrationFailedToUpdateDns` | A partition migration failed to update DNS |
| 408 Request Timeout | 1900 | `RequestPreempted` | The request was preempted |
| 408 Request Timeout (*inferred*) | 13009 | `ThinProxyGenerated408` | Generated by the thin client proxy |
| 409 Conflict | 1006 | `ConflictWithControlPlane` | The operation conflicts with a control plane operation |
| 409 Conflict | 3050 | `PartitionMigrationDocumentCountMismatchSourceTarget` | The document counts of the source and target partitions of a partition migration do not match |
| 409 Conflict | 3051 | `PartitionMigrationDocumentCountMismatchTargetReplicas` | The document counts of the target partition replicas of a partition migration do not match |
| 409 Conflict | 3206 | `DatabaseNameAlreadyExists` | A database with the name already exists |
| 409 Conflict | 3207 | `ConfigurationNameAlreadyExists` | A configuration with the name already exists |
| 409 Conflict | 3301 | `UniqueIndexConflict` | The item violates a unique key constraint |
| 409 Conflict | 3302 | `PartitionKeyHashCollisionForId` | The partition key hash collides for the id |
| 409 Conflict | 3303 | `AzureBackupVaultIncrementalBackupPaused` | Incremental backup to the Azure Backup vault is paused |
| 409 Conflict | 3304 | `AzureBackupVaultIncrementalBackupRestoreDisabled` | Restore from an incremental Azure Backup vault backup is disabled |
| 409 Conflict (*inferred*) | 5401 | `InitialRetriableWriteRequestCompleted` | The initial request of a retriable write has already completed |
| 409 Conflict (*inferred*) | 5402 | `DuplicateRetriableWriteRequest` | The retriable write request is a duplicate |
| 409 Conflict (*inferred*) | 5403 | `ConflictOperationInUserTransaction` | The operation conflicts with another operation in the user transaction |
| 409 Conflict (*inferred*) | 6300 | `CollectionTruncateNotAllowedDuringMerge` | Truncating a container is not allowed during a merge |
| 410 Gone | 1000 | `NameCacheIsStale` | The client's name cache is stale |
| 410 Gone | 1002 | `PartitionKeyRangeGone` | The partition key range is gone, for example after a split |
| 410 Gone | 1007 | `CompletingSplit` | A partition split is completing |
| 410 Gone | 1008 | `CompletingPartitionMigration` | A partition migration is completing |
| 410 Gone | 1022 | `LeaseNotFound` | The lease is not found |
| 410 Gone | 1024 | `ArchivalPartitionNotPresent` | The archival partition is not present |
| 412 Precondition Failed | 2001 | `SplitIsDisabled` | Splitting is disabled |
| 412 Precondition Failed | 2002 | `CollectionsInPartitionGotUpdated` | The containers in the partition were updated |
| 412 Precondition Failed | 2003 | `CannotAcquirePartitionKeyRangesLock` | The partition key ranges lock cannot be acquired |
| 412 Precondition Failed | 2004 | `ResourceNotFound` | The resource is not found |
| 412 Precondition Failed | 2005 | `CannotAcquireOfferOwnerLock` | The offer owner lock cannot be acquired |
| 412 Precondition Failed | 2007 | `CannotAcquirePartitionKeyRangeLock` | The partition key range lock cannot be acquired |
| 412 Precondition Failed | 2008 | `CannotAcquirePartitionLock` | The partition lock cannot be acquired |
| 412 Precondition Failed | 2011 | `StorageSplitConflictingWithNWayThroughputSplit` | A storage split conflicts with an n-way throughput split |
| 412 Precondition Failed | 2012 | `MergeIsDisabled` | Merging is disabled |
| 412 Precondition Failed | 2015 | `TombstoneRecordsNotFound` | The tombstone records are not found |
| 412 Precondition Failed | 2016 | `InvalidAccountStatus` | The account status is invalid |
| 412 Precondition Failed | 2017 | `OfferValidationFailed` | The throughput offer validation failed |
| 412 Precondition Failed | 2018 | `CannotAcquireMasterPartitionAccessLock` | The master partition access lock cannot be acquired |
| 412 Precondition Failed | 2019 | `CannotAcquireInAccountRestoreLock` | The in-account restore lock cannot be acquired |
| 412 Precondition Failed | 2020 | `CollectionStateChanged` | The container state changed |
| 412 Precondition Failed | 2021 | `OfferScaledUpByUser` | The throughput offer was scaled up by the user |
| 412 Precondition Failed | 2101 | `CannotAcquireLogStoreLoadBalanceLock` | The log store load balance lock cannot be acquired |
| 412 Precondition Failed | 5325 | `MismatchingCollectionRidsOnMigratePartitionDuringMigration` | The container resource ids do not match on a migrate partition request during a migration |
| 412 Precondition Failed | 5326 | `PartitionNotInMigratingStatusForMigratePartitionRequest` | The partition is not in the migrating status for a migrate partition request |
| 412 Precondition Failed | 5327 | `MissingPartitionResourceOnCompleteMigration` | The partition resource is missing when completing a migration |
| 412 Precondition Failed | 5328 | `MissingPartitionResourceOnAbortMigration` | The partition resource is missing when aborting a migration |
| 413 Request Entity Too Large | 3401 | `TransactionLimitExceeded` | The transaction limit is exceeded |
| 413 Request Entity Too Large | 3402 | `BatchResponseSizeExceeded` | The batch response size is exceeded |
| 429 Too Many Requests | 3073 | `BwTreeIoRateLimiter` | Throttled by the Bw-tree I/O rate limiter |
| 429 Too Many Requests | 3074 | `StalenessExceededBound` | The staleness exceeded its bound |
| 429 Too Many Requests | 3075 | `ReplicationQueueFull` | The replication queue is full |
| 429 Too Many Requests | 3076 | `BwTreeLogFullBackpressure` | Throttled by back pressure from a full Bw-tree log |
| 429 Too Many Requests | 3077 | `ConnectionRateLimiter` | Throttled by the connection rate limiter |
| 429 Too Many Requests | 3078 | `XpCompositeReplicator` | Throttled by the composite replicator |
| 429 Too Many Requests | 3079 | `Unexpected` | Throttled for an unexpected reason |
| 429 Too Many Requests | 3080 | `AsyncReaderWriterLock` | Throttled by an asynchronous reader-writer lock |
| 429 Too Many Requests | 3081 | `ServiceModule` | Throttled by a service module |
| 429 Too Many Requests | 3082 | `ValueDoesNotMatchExpectedBound` | A value does not match its expected bound |
| 429 Too Many Requests | 3083 | `SinkPartitionValueDoesNotMatchExpectedBound` | A sink partition value does not match its expected bound |
| 429 Too Many Requests | 3084 | `StoredProcedureConcurrency` | Too many concurrent stored procedure executions |
| 429 Too Many Requests | 3085 | `RntbdClientChannel` | Throttled by the RNTBD client channel |
| 429 Too Many Requests | 3086 | `LogFlushQueueDepthBackpressure` | Throttled by back pressure from the log flush queue depth |
| 429 Too Many Requests | 3087 | `CheckpointQueueDepthBackpressure` | Throttled by back pressure from the checkpoint queue depth |
| 429 Too Many Requests | 3088 | `ThrottleDueToSplit` | Throttled because of a partition split |
| 429 Too Many Requests | 3089 | `AeQueueFull` | The AE queue is full |
| 429 Too Many Requests | 3090 | `QuotaExceeded` | A quota is exceeded |
| 429 Too Many Requests | 3091 | `CollectionQuotaExceeded` | The container quota is exceeded |
| 429 Too Many Requests | 3092 | `SystemResourceUnavailable` | A system resource is unavailable |
| 429 Too Many Requests | 3093 | `PartitionedResourceQuotaExceeded` | The partitioned resource quota is exceeded |
| 429 Too Many Requests | 3094 | `ThrottleDueToResourceExhaustion` | Throttled because of resource exhaustion |
| 429 Too Many Requests | 3095 | `ThrottleDueToStagingIndexQueueFull` | Throttled because the staging index queue is full |
| 429 Too Many Requests | 3096 | `ThrottleDueToReplicationBackpressure` | Throttled by replication back pressure |
| 429 Too Many Requests | 3097 | `CollectionQuotaExceededAutopilot` | The container quota of an autoscale (autopilot) container is exceeded |
| 429 Too Many Requests | 3098 | `LogStoreNoFreeSegments` | The log store has no free segments |
| 429 Too Many Requests | 3099 | `ThrottledByBlobRead` | Throttled by a blob read |
| 429 Too Many Requests | 3100 | `OperationLogSizeTooBig` | The operation log is too big |
| 429 Too Many Requests | 3101 | `ArchivalPartitionPendingCatchup` | The archival partition has not caught up yet |
| 429 Too Many Requests | 3102 | `ThrottleDueToTrafficRegulation` | Throttled by traffic regulation |
| 429 Too Many Requests | 3103 | `ThrottleDueToTransportBufferUsage` | Throttled because of transport buffer usage |
| 429 Too Many Requests | 3200 | `RuBudgetExceeded` | The request units per second budget is exceeded |
| 429 Too Many Requests | 3201 | `GatewayThrottled` | The gateway throttled the request |
| 429 Too Many Requests | 3202 | `RupmPartitionLimitExceeded` | The request units per minute limit of the partition is exceeded |
| 429 Too Many Requests | 3203 | `RupmSharedBudgetExceeded` | The shared request units per minute budget is exceeded |
| 429 Too Many Requests | 3204 | `ThrottledOfferScaleDown` | Throttled because the throughput offer is scaling down |
| 429 Too Many Requests | 3207 | `PrepareTimeLimitExceeded` | The prepare time limit is exceeded |
| 429 Too Many Requests | 3208 | `ClientTcpChannelFull` | The client TCP channel is full |
| 429 Too Many Requests | 3209 | `BwTermCountLimitExceeded` | The Bw-tree term count limit is exceeded |
| 429 Too Many Requests | 3210 | `RuBudgetExceededForMaster` | The request units per second budget of the master partition is exceeded |
| 429 Too Many Requests | 3211 | `ThrottleDueToEncryptedRevokedStoreLogNotEmpty` | Throttled because the log of a revoked encrypted store is not empty |
| 429 Too Many Requests | 3212 | `ThroughputBucketMaxThroughputExceeded` | The throughput bucket exceeded its configured maximum throughput |
| 429 Too Many Requests | 3213 | `ThroughputBucketConfigurationChangeThrottled` | The throughput bucket configuration was changed more than once in 10 minutes |
| 429 Too Many Requests | 3214 | `HotPartitionKeyThrottled` | A hot partition key is throttled |
| 429 Too Many Requests | 3300 | `MicrosoftFabricCuBudgetExceeded` | The Microsoft Fabric capacity unit budget is exceeded |
| 429 Too Many Requests (*inferred*) | 13010 | `ThinProxyRequestThrottled` | The thin client proxy throttled the request |
| 449 Retry With | 5350 | `RbacAadGroupUnavailable` | The Microsoft Entra ID group of role-based access control is unavailable |
| 449 Retry With | 5351 | `AzureRbacAccessDecisionUnavailable` | The Azure role-based access control access decision is unavailable |
| 449 Retry With | 5352 | `DtcCoordinatorRaceConflict` | A distributed transaction coordinator race conflict occurred |
| 449 Retry With (*inferred*) | 5404 | `RetriableWriteRequestResponseExpiredInPrimaryCache` | The response of a retriable write request expired in the primary cache |
| 500 Internal Server Error | 3001 | `ConfigurationNameNotEmpty` | The configuration name is not empty |
| 500 Internal Server Error | 3002 | `ConfigurationOperationCancelled` | The configuration operation was cancelled |
| 500 Internal Server Error | 3003 | `InvalidAccountConfiguration` | The account configuration is invalid |
| 500 Internal Server Error | 3004 | `FederationDoesNotExistOrIsLocked` | The federation does not exist or is locked |
| 500 Internal Server Error | 3010 | `PartitionFailoverError` | A partition failover failed |
| 500 Internal Server Error | 3021 | `OperationManagerDequeuePumpStopped` | The operation manager dequeue pump stopped |
| 500 Internal Server Error | 3042 | `OperationCancelledWithNoRollback` | The operation was cancelled without a rollback |
| 500 Internal Server Error | 3043 | `SplitTimedOut` | A partition split timed out |
| 500 Internal Server Error | 5360 | `RbacDisabledDueToArmPath` | Role-based access control is disabled because of the Azure Resource Manager path |
| 500 Internal Server Error | 5411 | `DtcLedgerFailure` | The distributed transaction coordinator ledger failed |
| 500 Internal Server Error | 5412 | `DtcAccountConfigFailure` | The distributed transaction coordinator account configuration failed |
| 500 Internal Server Error | 5413 | `DtcDispatchFailure` | The distributed transaction coordinator dispatch failed |
| 500 Internal Server Error | 5415 | `DtcOperationRolledBack` | The distributed transaction coordinator rolled the operation back |
| 500 Internal Server Error (*inferred*) | 13011 | `ThinProxyGenerated500` | Generated by the thin client proxy |
| 503 Service Unavailable | 1007 | `InsufficientBindablePartitions` | There are not enough bindable partitions |
| 503 Service Unavailable | 1012 | `ComputeFederationNotFound` | The compute federation is not found |
| 503 Service Unavailable | 1337 | `GoneException` | A gone exception was not resolved by retries |
| 503 Service Unavailable | 1338 | `QuorumNotMet` | The quorum is not met |
| 503 Service Unavailable | 1339 | `TooManyTentativeWritesToSatelliteRegion` | There are too many tentative writes to a satellite region |
| 503 Service Unavailable | 6001 | `AggregatedHealthStateError` | The aggregated health state is an error |
| 503 Service Unavailable (*inferred*) | 6002 | `ApplicationHealthStateError` | The application health state is an error |
| 503 Service Unavailable (*inferred*) | 6003 | `HealthStateError` | The health state is an error |
| 503 Service Unavailable (*inferred*) | 6004 | `UnhealthyEventFound` | An unhealthy event was found |
| 503 Service Unavailable (*inferred*) | 6005 | `ClusterHealthEmpty` | The cluster health is empty |
| 503 Service Unavailable (*inferred*) | 6006 | `AllocationFailed` | An allocation failed |
| 503 Service Unavailable (*inferred*) | 6007 | `OperationResultNull` | The operation result is null |
| 503 Service Unavailable (*inferred*) | 6008 | `OperationResultUnexpected` | The operation result is unexpected |
| 503 Service Unavailable (*inferred*) | 6009 | `FabricNodesHealthError` | The health of the fabric nodes is an error |
| 503 Service Unavailable | 9001 | `OperationPaused` | The operation is paused |
| 503 Service Unavailable | 9002 | `ServiceIsOffline` | The service is offline |
| 503 Service Unavailable | 9003 | `InsufficientCapacity` | The capacity is insufficient |
| 503 Service Unavailable (*inferred*) | 13012 | `ThinProxyGenerated503` | Generated by the thin client proxy |
| 503 Service Unavailable | 20001 | `ClientConnectivityFailure` | The client failed to connect and all retries failed |
| 503 Service Unavailable | 20002 | `ClientTimeout` | The client timed out and all retries failed |
| 503 Service Unavailable | 20003 | `ClientOperatingSystemIoError` | An operating system I/O error occurred on the client |
| 503 Service Unavailable | 20004 | `ClientCpuOverload` | The client machine's CPU is overloaded |
| 503 Service Unavailable | 20005 | `ClientThreadStarvation` | The client machine's thread pool is starved |
| 503 Service Unavailable | 20006 | `ConnectionClosed` | The connection was interrupted or terminated unexpectedly |
| 503 Service Unavailable (*inferred*) | 20913 | `WriteRegionBarrierChangedMidOperation` | Generated by the .NET SDK: the write region barrier changed in the middle of the operation |
| 503 Service Unavailable | 21001 and greater | `TransientServiceConditionStart` | This and every greater sub-status is a transient service condition |
| 503 Service Unavailable | 21001 | `NameCacheIsStaleExceededRetryLimit` | The name cache stayed stale after the SDK retry limit |
| 503 Service Unavailable | 21002 | `PartitionKeyRangeGoneExceededRetryLimit` | The partition key range stayed gone after the SDK retry limit |
| 503 Service Unavailable | 21003 | `CompletingSplitExceededRetryLimit` | A partition split was still completing after the SDK retry limit |
| 503 Service Unavailable | 21004 | `CompletingPartitionMigrationExceededRetryLimit` | A partition migration was still completing after the SDK retry limit |
| 503 Service Unavailable | 21005 | `ServerGenerated410` | A 410 Gone from the service that SDK retries did not resolve |
| 503 Service Unavailable | 21006 | `GlobalStrongWriteBarrierNotMet` | The global strong write barrier is not met |
| 503 Service Unavailable | 21007 | `ReadQuorumNotMet` | The read quorum is not met |
| 503 Service Unavailable | 21008 | `ServerGenerated503` | A 503 Service Unavailable from the service |
| 503 Service Unavailable | 21009 | `NoValidStoreResponse` | No valid store response was received |
| 503 Service Unavailable | 21011 | `BarrierThrottled` | The barrier request was throttled |
| 503 Service Unavailable | 21012 | `NRegionCommitWriteBarrierNotMet` | The n-region commit write barrier is not met |
| 503 Service Unavailable | 21013 | `WriteBarrierThrottled` | The write barrier request was throttled |
| 503 Service Unavailable | 21015 | `RoutingMapSnapshotInconsistent` | The routing map snapshot is inconsistent |
| 404 / 400 | 10004 | `ThroughputOfferNotFound` | Generated by the client SDK, not the service, when the container has no throughput offer: the Python SDK pairs it with 404, the Java SDK with 400 |

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
