namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type PipelineSearchFilters =
    { SearchTerm: string option
      Tag: string option
      MarketType: string option
      Status: PipelineStatus option
      SortBy: string }

type PipelineSearchResult =
    { Pipelines: Pipeline list
      TotalCount: int }

type GetPipelineById = PipelineId -> CancellationToken -> Task<Result<Pipeline, ServiceError>>
type GetAllPipelines = CancellationToken -> Task<Result<Pipeline list, ServiceError>>
type CreatePipeline = Pipeline -> CancellationToken -> Task<Result<Pipeline, ServiceError>>
type UpdatePipeline = Pipeline -> CancellationToken -> Task<Result<Pipeline, ServiceError>>
type DeletePipeline = PipelineId -> CancellationToken -> Task<Result<unit, ServiceError>>
type CountPipelines = CancellationToken -> Task<Result<int, ServiceError>>
type CountEnabledPipelines = CancellationToken -> Task<Result<int, ServiceError>>
type GetAllPipelineTags = CancellationToken -> Task<Result<string list, ServiceError>>
type SearchPipelines = PipelineSearchFilters -> int -> int -> CancellationToken -> Task<Result<PipelineSearchResult, ServiceError>>

type PipelinePorts =
    { GetById: GetPipelineById
      GetAll: GetAllPipelines
      Create: CreatePipeline
      Update: UpdatePipeline
      Delete: DeletePipeline
      Count: CountPipelines
      CountEnabled: CountEnabledPipelines
      GetAllTags: GetAllPipelineTags
      Search: SearchPipelines }
