namespace Plutus.Core.Ports

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared.Errors

type CreateBacktestRun = BacktestConfig -> CancellationToken -> Task<Result<BacktestRunId, ServiceError>>
type UpdateBacktestRunResults = BacktestRun -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBacktestRunById = BacktestRunId -> CancellationToken -> Task<Result<BacktestRun option, ServiceError>>
type GetBacktestRunsByPipeline = PipelineId -> CancellationToken -> Task<Result<BacktestRun list, ServiceError>>
type InsertBacktestTrades = BacktestTrade list -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBacktestTradesByRun = BacktestRunId -> CancellationToken -> Task<Result<BacktestTrade list, ServiceError>>
type InsertBacktestEquityPoints = BacktestEquityPoint list -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBacktestEquityByRun = BacktestRunId -> CancellationToken -> Task<Result<BacktestEquityPoint list, ServiceError>>
type InsertBacktestLogs = BacktestExecutionLog list -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBacktestExecutionSummaries = BacktestRunId -> int -> int -> CancellationToken -> Task<Result<BacktestExecutionSummary list * int, ServiceError>>
type GetBacktestLogsByRun = BacktestRunId -> CancellationToken -> Task<Result<BacktestExecutionLog list, ServiceError>>
type GetAllBacktestRuns = int -> int -> CancellationToken -> Task<Result<BacktestRun list * int, ServiceError>>
type CountBacktestRuns = CancellationToken -> Task<Result<int, ServiceError>>
type DeleteBacktestRun = BacktestRunId -> CancellationToken -> Task<Result<unit, ServiceError>>
type GetBacktestLogsByExecution = BacktestRunId -> ExecutionId -> CancellationToken -> Task<Result<BacktestExecutionLog list, ServiceError>>

type BacktestPorts =
    { CreateRun: CreateBacktestRun
      UpdateRunResults: UpdateBacktestRunResults
      GetRunById: GetBacktestRunById
      GetRunsByPipeline: GetBacktestRunsByPipeline
      InsertTrades: InsertBacktestTrades
      GetTradesByRun: GetBacktestTradesByRun
      InsertEquityPoints: InsertBacktestEquityPoints
      GetEquityByRun: GetBacktestEquityByRun
      InsertLogs: InsertBacktestLogs
      GetExecutionSummaries: GetBacktestExecutionSummaries
      GetLogsByRun: GetBacktestLogsByRun
      GetAllRuns: GetAllBacktestRuns
      CountRuns: CountBacktestRuns
      DeleteRun: DeleteBacktestRun
      GetLogsByExecution: GetBacktestLogsByExecution }
