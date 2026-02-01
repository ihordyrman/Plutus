namespace Warehouse.Core.Markets.Abstractions

open System.Threading
open System.Threading.Tasks
open Warehouse.Core.Domain
open Warehouse.Core.Shared

module BalanceProvider =
    open Errors

    type T =
        { MarketType: MarketType
          GetBalances: CancellationToken -> Task<Result<BalanceSnapshot, ServiceError>>
          GetBalance: string -> CancellationToken -> Task<Result<Balance, ServiceError>>
          GetTotalUsdtValue: CancellationToken -> Task<Result<decimal, ServiceError>> }
