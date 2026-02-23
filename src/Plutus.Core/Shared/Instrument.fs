namespace Plutus.Core.Shared

type Instrument =
    { Base: string
      Quote: string }

    override this.ToString() = $"%s{this.Base}-%s{this.Quote}"
