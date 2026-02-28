namespace Plutus.App.Pages.Orders

open System
open System.Data
open System.Threading
open System.Threading.Tasks
open Falco
open Falco.Htmx
open Falco.Markup
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Plutus.Core.Domain
open Plutus.Core.Repositories

type OrderListItem =
    { Id: int64
      PipelineId: int option
      Instrument: string
      Side: OrderSide
      Status: OrderStatus
      MarketType: MarketType
      Quantity: decimal
      Price: decimal option
      Fee: decimal option
      CreatedAt: DateTime
      ExecutedAt: DateTime option }

type OrdersGridData =
    { Orders: OrderListItem list
      TotalCount: int
      Page: int
      PageSize: int }

    static member Empty = { Orders = []; TotalCount = 0; Page = 1; PageSize = 20 }

type OrderFilters =
    { SearchTerm: string option
      Side: string option
      Status: string option
      MarketType: string option
      SortBy: string
      Page: int
      PageSize: int }

    static member Empty =
        { SearchTerm = None
          Side = None
          Status = None
          MarketType = None
          SortBy = "created-desc"
          Page = 1
          PageSize = 20 }

module Data =
    let getCount (scopeFactory: IServiceScopeFactory) (ct: CancellationToken) : Task<int> =
        task {
            use scope = scopeFactory.CreateScope()
            let db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            match! OrderRepository.count db ct with
            | Ok n -> return n
            | Error _ -> return 0
        }

    let getFilteredOrders
        (scopeFactory: IServiceScopeFactory)
        (filters: OrderFilters)
        (ct: CancellationToken)
        : Task<OrdersGridData>
        =
        task {
            use scope = scopeFactory.CreateScope()
            let db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

            let parseSide =
                match filters.Side with
                | Some "buy" -> Some OrderSide.Buy
                | Some "sell" -> Some OrderSide.Sell
                | _ -> None

            let parseStatus =
                match filters.Status with
                | Some "pending" -> Some OrderStatus.Pending
                | Some "placed" -> Some OrderStatus.Placed
                | Some "partially-filled" -> Some OrderStatus.PartiallyFilled
                | Some "filled" -> Some OrderStatus.Filled
                | Some "cancelled" -> Some OrderStatus.Cancelled
                | Some "failed" -> Some OrderStatus.Failed
                | _ -> None

            let parseMarketType =
                match filters.MarketType with
                | Some mt ->
                    match Enum.TryParse<MarketType>(mt, true) with
                    | true, v -> Some v
                    | false, _ -> None
                | None -> None

            let searchFilters: SearchFilters =
                { SearchTerm = filters.SearchTerm
                  Side = parseSide
                  Status = parseStatus
                  MarketType = parseMarketType
                  SortBy = filters.SortBy }

            let skip = (filters.Page - 1) * filters.PageSize

            match! OrderRepository.search db searchFilters skip filters.PageSize ct with
            | Error _ -> return OrdersGridData.Empty
            | Ok result ->
                let orderItems =
                    result.Orders
                    |> List.map (fun o ->
                        { Id = o.Id
                          PipelineId = o.PipelineId
                          Instrument = string o.Instrument
                          Side = o.Side
                          Status = o.Status
                          MarketType = o.MarketType
                          Quantity = o.Quantity
                          Price = o.Price
                          Fee = o.Fee
                          CreatedAt = o.CreatedAt
                          ExecutedAt = o.ExecutedAt }
                    )

                return
                    { Orders = orderItems
                      TotalCount = result.TotalCount
                      Page = filters.Page
                      PageSize = filters.PageSize }
        }

module View =
    let private filterSelect name label options =
        _div
            [ _class_ "min-w-[120px]" ]
            [ _label [ _class_ "block text-xs font-medium text-slate-500 mb-1" ] [ Text.raw label ]
              _select
                  [ _name_ name
                    _class_
                        "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ]
                  [ yield _option [ _value_ "" ] [ Text.raw $"All" ]
                    for (value, text) in options do
                        yield _option [ _value_ value ] [ Text.raw text ] ] ]

    let private sectionHeader =
        _div
            [ _class_ "flex justify-between items-center mb-6" ]
            [ _div
                  []
                  [ _h1 [ _class_ "text-lg font-semibold text-slate-900" ] [ Text.raw "Orders History" ]
                    _p [ _class_ "text-slate-400 text-sm" ] [ Text.raw "View and filter your trading orders" ] ] ]

    let private filterBar =
        _div
            [ _id_ "orders-filter-form"; _class_ "card mb-6" ]
            [ _form
                  [ Hx.get "/orders/table"
                    Hx.targetCss "#orders-table-container"
                    Hx.trigger "change, keyup delay:300ms from:input"
                    Hx.swapOuterHtml
                    Hx.includeThis ]
                  [ _input [ _type_ "hidden"; _name_ "page"; _value_ "1" ]
                    _div
                        [ _class_ "flex flex-wrap gap-4" ]
                        [ _div
                              [ _class_ "flex-1 min-w-[200px]" ]
                              [ _label
                                    [ _class_ "block text-xs font-medium text-slate-500 mb-1" ]
                                    [ Text.raw "Search Instrument" ]
                                _input
                                    [ _type_ "text"
                                      _name_ "searchTerm"
                                      _placeholder_ "Search by instrument..."
                                      _class_
                                          "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ] ]
                          filterSelect "filterSide" "Side" [ ("buy", "Buy"); ("sell", "Sell") ]
                          filterSelect
                              "filterStatus"
                              "Status"
                              [ ("pending", "Pending")
                                ("placed", "Placed")
                                ("partially-filled", "Partially Filled")
                                ("filled", "Filled")
                                ("cancelled", "Cancelled")
                                ("failed", "Failed") ]
                          filterSelect
                              "sortBy"
                              "Sort By"
                              [ ("created-desc", "Newest First")
                                ("created", "Oldest First")
                                ("instrument", "Instrument A-Z")
                                ("instrument-desc", "Instrument Z-A")
                                ("quantity-desc", "Quantity High")
                                ("quantity", "Quantity Low")
                                ("status", "Status") ] ] ] ]

    let private tableHeader =
        _thead
            []
            [ _tr
                  []
                  [ for (text, align) in
                        [ "ID", "left"
                          "Pipeline", "left"
                          "Instrument", "left"
                          "Side", "left"
                          "Status", "left"
                          "Quantity", "right"
                          "Price", "right"
                          "Fee", "right"
                          "Created", "left" ] do
                        _th
                            [ _class_
                                  $"px-4 py-3 text-{align} text-xs font-medium text-slate-400 uppercase tracking-wider" ]
                            [ Text.raw text ] ] ]

    let private statusBadge (status: OrderStatus) =
        let (bgClass, textClass, label) =
            match status with
            | OrderStatus.Pending -> "bg-yellow-50", "text-yellow-700", "Pending"
            | OrderStatus.Placed -> "bg-blue-50", "text-blue-700", "Placed"
            | OrderStatus.PartiallyFilled -> "bg-indigo-50", "text-indigo-700", "Partial"
            | OrderStatus.Filled -> "bg-green-50", "text-green-700", "Filled"
            | OrderStatus.Cancelled -> "bg-slate-50", "text-slate-500", "Cancelled"
            | OrderStatus.Failed -> "bg-red-50", "text-red-700", "Failed"
            | _ -> "bg-slate-50", "text-slate-500", "Unknown"

        _span [ _class_ $"px-2 py-0.5 text-xs font-medium rounded {bgClass} {textClass}" ] [ Text.raw label ]

    let private sideBadge (side: OrderSide) =
        let (bgClass, textClass, label) =
            match side with
            | OrderSide.Buy -> "bg-green-50", "text-green-700", "Buy"
            | OrderSide.Sell -> "bg-red-50", "text-red-700", "Sell"
            | _ -> "bg-slate-50", "text-slate-500", "Unknown"

        _span [ _class_ $"px-2 py-0.5 text-xs font-medium rounded {bgClass} {textClass}" ] [ Text.raw label ]

    let private formatDecimal (value: decimal option) =
        match value with
        | Some v -> v.ToString("N4")
        | None -> "-"

    let private formatPipelineId (pipelineId: int option) =
        match pipelineId with
        | Some id -> string id
        | None -> "-"

    let orderRow (order: OrderListItem) =
        _tr
            [ _id_ $"order-{order.Id}"; _class_ "hover:bg-slate-50" ]
            [ _td [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ] [ Text.raw (string order.Id) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ Text.raw (formatPipelineId order.PipelineId) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap" ]
                  [ _span [ _class_ "font-medium text-slate-900 text-sm" ] [ Text.raw order.Instrument ] ]
              _td [ _class_ "px-4 py-3 whitespace-nowrap" ] [ sideBadge order.Side ]
              _td [ _class_ "px-4 py-3 whitespace-nowrap" ] [ statusBadge order.Status ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-right text-sm text-slate-900" ]
                  [ Text.raw (order.Quantity.ToString("N4")) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-right text-sm text-slate-500" ]
                  [ Text.raw (formatDecimal order.Price) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-right text-sm text-slate-500" ]
                  [ Text.raw (formatDecimal order.Fee) ]
              _td
                  [ _class_ "px-4 py-3 whitespace-nowrap text-sm text-slate-500" ]
                  [ Text.raw (order.CreatedAt.ToString("MMM dd, HH:mm")) ] ]

    let emptyState =
        _tr
            []
            [ _td
                  [ _colspan_ "9"; _class_ "px-4 py-12 text-center" ]
                  [ _div
                        [ _class_ "text-slate-400" ]
                        [ _i [ _class_ "fas fa-receipt text-3xl mb-3" ] []
                          _p [ _class_ "text-sm font-medium" ] [ Text.raw "No orders found" ]
                          _p [ _class_ "text-xs" ] [ Text.raw "Orders will appear here when you start trading" ] ] ] ]

    let loadingState =
        _tr
            []
            [ _td
                  [ _colspan_ "9"; _class_ "px-4 py-8 text-center text-slate-400" ]
                  [ _i [ _class_ "fas fa-spinner fa-spin text-lg mb-2" ] []
                    _p [ _class_ "text-sm" ] [ Text.raw "Loading orders..." ] ] ]

    let private paginationControls (data: OrdersGridData) =
        let totalPages = int (Math.Ceiling(float data.TotalCount / float data.PageSize))
        let hasPrev = data.Page > 1
        let hasNext = data.Page < totalPages
        let startRecord = if data.TotalCount = 0 then 0 else (data.Page - 1) * data.PageSize + 1
        let endRecord = min (data.Page * data.PageSize) data.TotalCount

        let enabledBtnClass = "bg-white text-slate-600 border border-slate-200 hover:bg-slate-50"
        let disabledBtnClass = "bg-slate-50 text-slate-300 cursor-not-allowed"
        let prevBtnClass = if hasPrev then enabledBtnClass else disabledBtnClass
        let nextBtnClass = if hasNext then enabledBtnClass else disabledBtnClass

        _div
            [ _class_ "flex items-center justify-between px-4 py-3 border-t border-slate-100" ]
            [ _div
                  [ _class_ "text-xs text-slate-400" ]
                  [ Text.raw $"Showing {startRecord} to {endRecord} of {data.TotalCount} orders" ]
              _div
                  [ _class_ "flex gap-2" ]
                  [ _button
                        [ _type_ "button"
                          _class_ $"px-3 py-1 text-xs font-medium rounded-md {prevBtnClass}"
                          if hasPrev then
                              Hx.get $"/orders/table?page={data.Page - 1}"
                              Hx.targetCss "#orders-table-container"
                              Hx.swapOuterHtml
                              Attr.create "hx-include" "#orders-filter-form form"
                          else
                              _disabled_ ]
                        [ Text.raw "Previous" ]
                    _span
                        [ _class_ "px-3 py-1 text-xs text-slate-400" ]
                        [ Text.raw $"Page {data.Page} of {totalPages}" ]
                    _button
                        [ _type_ "button"
                          _class_ $"px-3 py-1 text-xs font-medium rounded-md {nextBtnClass}"
                          if hasNext then
                              Hx.get $"/orders/table?page={data.Page + 1}"
                              Hx.targetCss "#orders-table-container"
                              Hx.swapOuterHtml
                              Attr.create "hx-include" "#orders-filter-form form"
                          else
                              _disabled_ ]
                        [ Text.raw "Next" ] ] ]

    let tableBody (data: OrdersGridData) =
        let rows =
            match data.Orders with
            | [] -> [ emptyState ]
            | orders -> orders |> List.map orderRow

        _div
            [ _id_ "orders-table-container" ]
            [ _div
                  [ _class_ "overflow-x-auto" ]
                  [ _table
                        [ _class_ "min-w-full divide-y divide-slate-100" ]
                        [ tableHeader; _tbody [ _class_ "bg-white divide-y divide-slate-100" ] rows ] ]
              paginationControls data ]

    let private ordersTable =
        _div
            [ _class_ "card overflow-hidden" ]
            [ _div
                  [ _id_ "orders-table-container"; Hx.get "/orders/table"; Hx.trigger "load"; Hx.swapOuterHtml ]
                  [ _div
                        [ _class_ "overflow-x-auto" ]
                        [ _table
                              [ _class_ "min-w-full divide-y divide-slate-100" ]
                              [ tableHeader; _tbody [ _class_ "bg-white divide-y divide-slate-100" ] [ loadingState ] ] ] ] ]

    let section = _section [] [ sectionHeader; filterBar; ordersTable ]

    let count (n: int) = Text.raw (string n)

module Handler =
    let count: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    let! count = Data.getCount scopeFactory ctx.RequestAborted
                    return! Response.ofHtml (View.count count) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Orders")
                    logger.LogError(ex, "Error getting orders count")
                    return! Response.ofHtml (View.count 0) ctx
            }

    let grid: HttpHandler =
        fun ctx ->
            task {
                try
                    return! Response.ofHtml View.section ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Orders")
                    logger.LogError(ex, "Error getting orders grid")
                    return! Response.ofHtml View.section ctx
            }

    let tryGetQueryStringValue (query: IQueryCollection) (key: string) : string option =
        match query.TryGetValue key with
        | true, values when values.Count > 0 && not (String.IsNullOrEmpty values.[0]) -> Some values.[0]
        | _ -> None

    let tryGetQueryStringInt (query: IQueryCollection) (key: string) (defaultValue: int) : int =
        match query.TryGetValue key with
        | true, values when values.Count > 0 ->
            match Int32.TryParse values.[0] with
            | true, v -> v
            | false, _ -> defaultValue
        | _ -> defaultValue

    let table: HttpHandler =
        fun ctx ->
            task {
                try
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()

                    let filters: OrderFilters =
                        { SearchTerm = tryGetQueryStringValue ctx.Request.Query "searchTerm"
                          Side = tryGetQueryStringValue ctx.Request.Query "filterSide"
                          Status = tryGetQueryStringValue ctx.Request.Query "filterStatus"
                          MarketType = tryGetQueryStringValue ctx.Request.Query "filterMarketType"
                          SortBy =
                            tryGetQueryStringValue ctx.Request.Query "sortBy" |> Option.defaultValue "created-desc"
                          Page = tryGetQueryStringInt ctx.Request.Query "page" 1
                          PageSize = tryGetQueryStringInt ctx.Request.Query "pageSize" 20 }

                    let! data = Data.getFilteredOrders scopeFactory filters ctx.RequestAborted
                    return! Response.ofHtml (View.tableBody data) ctx
                with ex ->
                    let logger = ctx.Plug<ILoggerFactory>().CreateLogger("Orders")
                    logger.LogError(ex, "Error getting filtered orders")
                    return! Response.ofHtml (View.tableBody OrdersGridData.Empty) ctx
            }
