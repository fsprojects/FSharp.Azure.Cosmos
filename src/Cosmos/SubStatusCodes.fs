namespace FSharp.Azure.Cosmos

/// <summary>
/// Cosmos DB sub-status codes documented on Microsoft Learn, returned in the <c>x-ms-substatus</c> header.
/// <para>
/// A sub-status only has a meaning together with the HTTP status code: some numbers are reused for different
/// statuses, for example 1002 is <see cref="SubStatusCodes.ReadSessionNotAvailable"/> with 404 and
/// <see cref="SubStatusCodes.PartitionKeyRangeGone"/> with 410. Match on both.
/// </para>
/// <para>
/// Sources: the REST API HTTP status codes page, the Azure Cosmos DB troubleshooting guides, the throughput buckets
/// FAQ and the <c>SubStatusCodes</c> reference of the Python SDK. Codes without a documented HTTP status are
/// listed as documented, without one.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module SubStatusCodes =

    /// No sub-status: the response has no <c>x-ms-substatus</c> header or it is 0.
    [<Literal>]
    let Unknown = 0

    /// With 403 Forbidden: a write was sent to a region that no longer accepts writes during a manual failover.
    [<Literal>]
    let WriteForbidden = 3

    /// With 410 Gone: the client's name cache is stale.
    [<Literal>]
    let NameCacheIsStale = 1000

    /// With 400 Bad Request: the partition key does not match the item or the container's partition key definition.
    [<Literal>]
    let PartitionKeyMismatch = 1001

    /// With 404 Not Found: the read session is not available for the session token.
    [<Literal>]
    let ReadSessionNotAvailable = 1002

    /// With 410 Gone: the partition key range is gone, for example after a split.
    [<Literal>]
    let PartitionKeyRangeGone = 1002

    /// With 404 Not Found: the owner resource, a database or container, does not exist.
    [<Literal>]
    let OwnerResourceNotFound = 1003

    /// With 400 Bad Request: the cross-partition query cannot be served.
    [<Literal>]
    let CrossPartitionQueryNotServable = 1004

    /// With 403 Forbidden: the provisioning limit is reached.
    [<Literal>]
    let ProvisionLimitReached = 1005

    /// The operation conflicts with a control plane operation.
    [<Literal>]
    let ConflictWithControlPlane = 1006

    /// With 410 Gone: a partition split is completing.
    [<Literal>]
    let CompletingSplit = 1007

    /// With 503 Service Unavailable: there are not enough bindable partitions.
    [<Literal>]
    let InsufficientBindablePartitions = 1007

    /// With 410 Gone: a partition migration is completing.
    [<Literal>]
    let CompletingPartitionMigration = 1008

    /// With 403 Forbidden: the database account is not found.
    [<Literal>]
    let DatabaseAccountNotFound = 1008

    /// The container definition in the request is the same as the existing one.
    [<Literal>]
    let RedundantCollectionPut = 1009

    /// The quota of containers in a shared throughput database is exceeded.
    [<Literal>]
    let SharedThroughputDatabaseQuotaExceeded = 1010

    /// The shared throughput offer does not need to grow.
    [<Literal>]
    let SharedThroughputOfferGrowNotNeeded = 1011

    /// With 404 Not Found: the container create operation is still in progress; retry the read until it succeeds.
    [<Literal>]
    let ContainerCreateInProgress = 1013

    /// With 403 Forbidden: the logical partition reached its maximum size.
    [<Literal>]
    let PartitionKeyQuotaExceeded = 1014

    /// The container resource id does not match.
    [<Literal>]
    let CollectionRidMismatch = 1024

    /// With 429 Too Many Requests: the throughput bucket exceeded its configured maximum throughput.
    [<Literal>]
    let ThroughputBucketMaxThroughputExceeded = 3212

    /// With 429 Too Many Requests: the throughput bucket configuration was changed more than once in 10 minutes.
    [<Literal>]
    let ThroughputBucketConfigurationChangeThrottled = 3213

    /// Customer-managed keys: Azure Cosmos DB cannot get the Microsoft Entra ID access token for the Key Vault.
    [<Literal>]
    let AadTokenAcquisitionFailed = 4000

    /// Customer-managed keys: the Microsoft Entra ID service is unavailable.
    [<Literal>]
    let AadServiceUnavailable = 4001

    /// Customer-managed keys: the Key Vault does not grant Azure Cosmos DB access, or the key is disabled.
    [<Literal>]
    let KeyVaultAccessDenied = 4002

    /// Customer-managed keys: the key is not found in the Key Vault.
    [<Literal>]
    let KeyVaultKeyNotFound = 4003

    /// Customer-managed keys: the Key Vault service is unavailable.
    [<Literal>]
    let KeyVaultServiceUnavailable = 4004

    /// Customer-managed keys: the Key Vault cannot wrap or unwrap the key.
    [<Literal>]
    let KeyVaultWrapUnwrapFailure = 4005

    /// Customer-managed keys: the Key Vault key URL is invalid, for example it includes the key version.
    [<Literal>]
    let InvalidKeyVaultKeyUrl = 4006

    /// Customer-managed keys: internal server error, the input bytes are not in the base64 format.
    [<Literal>]
    let InvalidInputBytes = 4007

    /// Customer-managed keys: the Key Vault returned an internal service error.
    [<Literal>]
    let KeyVaultInternalServerError = 4008

    /// Customer-managed keys: the Key Vault DNS name cannot be resolved.
    [<Literal>]
    let KeyVaultDnsNotResolved = 4009

    /// With 403 Forbidden: the request cannot be authorized by a Microsoft Entra ID token in the data plane.
    [<Literal>]
    let AadRequestNotAuthorized = 5300

    /// The throughput offer is not found.
    [<Literal>]
    let ThroughputOfferNotFound = 10004

    /// With 503 Service Unavailable: the client failed to connect and all retries failed.
    [<Literal>]
    let ClientConnectivityFailure = 20001

    /// With 503 Service Unavailable: the client timed out and all retries failed.
    [<Literal>]
    let ClientTimeout = 20002

    /// With 503 Service Unavailable: an operating system I/O error occurred on the client.
    [<Literal>]
    let ClientOperatingSystemIoError = 20003

    /// With 503 Service Unavailable: the client machine's CPU is overloaded.
    [<Literal>]
    let ClientCpuOverload = 20004

    /// With 503 Service Unavailable: the client machine's thread pool is starved.
    [<Literal>]
    let ClientThreadStarvation = 20005

    /// With 503 Service Unavailable: the connection was interrupted or terminated unexpectedly.
    [<Literal>]
    let ConnectionClosed = 20006

    /// With 503 Service Unavailable: this and every greater sub-status is a transient service condition.
    [<Literal>]
    let TransientServiceConditionStart = 21001

    /// With 503 Service Unavailable: the routing map snapshot is inconsistent.
    [<Literal>]
    let RoutingMapSnapshotInconsistent = 21015
