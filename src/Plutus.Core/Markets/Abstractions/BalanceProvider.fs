namespace Plutus.Core.Markets.Abstractions

open System.Threading
open System.Threading.Tasks
open Plutus.Core.Domain
open Plutus.Core.Shared

module BalanceProvider =
    open Errors

    type T =
        { MarketType: MarketType
          GetBalances: CancellationToken -> Task<Result<BalanceSnapshot, ServiceError>>
          GetBalance: string -> CancellationToken -> Task<Result<Balance, ServiceError>>
          GetTotalUsdtValue: CancellationToken -> Task<Result<decimal, ServiceError>> }
