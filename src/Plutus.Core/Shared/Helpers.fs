namespace Plutus.Core.Shared

module Helpers =
    let foldAsync folder state items =
        let rec loop acc =
            function
            | [] -> task { return acc }
            | x :: xs ->
                task {
                    let! newAcc = folder acc x
                    return! loop newAcc xs
                }

        loop state items
