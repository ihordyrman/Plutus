namespace Plutus.Core.Ports

open System
open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type CreateSyncJob = SyncJob -> CancellationToken -> Task<Result<SyncJob, ServiceError>>
type UpdateSyncJobStatus = SyncJobId -> SyncJobStatus -> string option -> CancellationToken -> Task<Result<unit, ServiceError>>
type UpdateSyncJobProgress = SyncJobId -> int -> DateTimeOffset -> CancellationToken -> Task<Result<unit, ServiceError>>
type DeleteSyncJob = SyncJobId -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetActiveSyncJobs = CancellationToken -> Task<Result<SyncJob list, ServiceError>>
type GetAllSyncJobs = CancellationToken -> Task<Result<SyncJob list, ServiceError>>

type SyncJobPorts =
    { Create: CreateSyncJob
      UpdateStatus: UpdateSyncJobStatus
      UpdateProgress: UpdateSyncJobProgress
      Delete: DeleteSyncJob
      GetActive: GetActiveSyncJobs
      GetAll: GetAllSyncJobs }
