namespace Plutus.Core.Markets.Abstractions

open System
open Plutus.Core.Domain

[<CLIMutable>]
type Balance =
    { Currency: string
      Available: decimal
      Total: decimal
      Frozen: decimal
      InOrder: decimal
      MarketType: MarketType
      UpdatedAt: DateTime }

[<CLIMutable>]
type AccountBalance =
    { MarketType: MarketType
      TotalEquity: decimal
      AvailableBalance: decimal
      UsedMargin: decimal
      UnrealizedPnl: decimal
      Balances: Balance list
      UpdatedAt: DateTime }

[<CLIMutable>]
type BalanceSnapshot =
    { MarketType: MarketType
      Spot: Map<string, Balance>
      Funding: Map<string, Balance>
      mutable AccountSummary: AccountBalance option
      Timestamp: DateTime }

