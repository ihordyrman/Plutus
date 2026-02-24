namespace Plutus.Core.Shared

type Instrument =
    { Base: string
      Quote: string }

    override this.ToString() = $"%s{this.Base}-%s{this.Quote}"

module Instrument =
    let parse (s: string) =
        match s.Split '-' with
        | [| baseCcy; quoteCcy |] -> { Base = baseCcy; Quote = quoteCcy }
        | _ -> failwith $"Invalid instrument format: {s}"
