namespace Warehouse.App.Pages.Orders

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
open Warehouse.Core.Domain
open Warehouse.Core.Repositories

type OrderListItem =
    { Id: int64
      PipelineId: int option
      Symbol: string
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
                          PipelineId = if o.PipelineId.HasValue then Some o.PipelineId.Value else None
                          Symbol = o.Symbol
                          Side = o.Side
                          Status = o.Status
                          MarketType = o.MarketType
                          Quantity = o.Quantity
                          Price = if o.Price.HasValue then Some o.Price.Value else None
                          Fee = if o.Fee.HasValue then Some o.Fee.Value else None
                          CreatedAt = o.CreatedAt
                          ExecutedAt = if o.ExecutedAt.HasValue then Some o.ExecutedAt.Value else None }
                    )

                return
                    { Orders = orderItems
                      TotalCount = result.TotalCount
                      Page = filters.Page
                      PageSize = filters.PageSize }
        }

module View =
    let private filterSelect name label options =
        _div [ _class_ "min-w-[120px]" ] [
            _label [ _class_ "block text-sm font-medium text-gray-700 mb-1" ] [ Text.raw label ]
            _select [
                _name_ name
                _class_
                    "w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            ] [
                yield _option [ _value_ "" ] [ Text.raw $"All" ]
                for (value, text) in options do
                    yield _option [ _value_ value ] [ Text.raw text ]
            ]
        ]

    let private sectionHeader =
        _div [ _class_ "flex justify-between items-center mb-6" ] [
            _div [] [
                _h1 [ _class_ "text-2xl font-bold text-gray-900" ] [ Text.raw "Orders History" ]
                _p [ _class_ "text-gray-600" ] [ Text.raw "View and filter your trading orders" ]
            ]
        ]

    let private filterBar =
        _div [ _id_ "orders-filter-form"; _class_ "card mb-6" ] [
            _form [
                Hx.get "/orders/table"
                Hx.targetCss "#orders-table-container"
                Hx.trigger "change, keyup delay:300ms from:input"
                Hx.swapOuterHtml
                Hx.includeThis
            ] [
                _input [ _type_ "hidden"; _name_ "page"; _value_ "1" ]
                _div [ _class_ "flex flex-wrap gap-4" ] [
                    _div [ _class_ "flex-1 min-w-[200px]" ] [
                        _label [ _class_ "block text-sm font-medium text-gray-700 mb-1" ] [ Text.raw "Search Symbol" ]
                        _input [
                            _type_ "text"
                            _name_ "searchTerm"
                            Attr.create "placeholder" "Search by symbol..."
                            _class_
                                "w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                        ]
                    ]
                    filterSelect "filterSide" "Side" [ ("buy", "Buy"); ("sell", "Sell") ]
                    filterSelect "filterStatus" "Status" [
                        ("pending", "Pending")
                        ("placed", "Placed")
                        ("partially-filled", "Partially Filled")
                        ("filled", "Filled")
                        ("cancelled", "Cancelled")
                        ("failed", "Failed")
                    ]
                    filterSelect "sortBy" "Sort By" [
                        ("created-desc", "Newest First")
                        ("created", "Oldest First")
                        ("symbol", "Symbol A-Z")
                        ("symbol-desc", "Symbol Z-A")
                        ("quantity-desc", "Quantity High")
                        ("quantity", "Quantity Low")
                        ("status", "Status")
                    ]
                ]
            ]
        ]

    let private tableHeader =
        _thead [ _class_ "bg-gray-50" ] [
            _tr [] [
                for (text, align) in
                    [ "ID", "left"
                      "Pipeline", "left"
                      "Symbol", "left"
                      "Side", "left"
                      "Status", "left"
                      "Quantity", "right"
                      "Price", "right"
                      "Fee", "right"
                      "Created", "left" ] do
                    _th [ _class_ $"px-6 py-3 text-{align} text-xs font-medium text-gray-500 uppercase tracking-wider" ] [
                        Text.raw text
                    ]
            ]
        ]

    let private statusBadge (status: OrderStatus) =
        let (bgClass, textClass, label) =
            match status with
            | OrderStatus.Pending -> "bg-yellow-100", "text-yellow-800", "Pending"
            | OrderStatus.Placed -> "bg-blue-100", "text-blue-800", "Placed"
            | OrderStatus.PartiallyFilled -> "bg-indigo-100", "text-indigo-800", "Partial"
            | OrderStatus.Filled -> "bg-green-100", "text-green-800", "Filled"
            | OrderStatus.Cancelled -> "bg-gray-100", "text-gray-600", "Cancelled"
            | OrderStatus.Failed -> "bg-red-100", "text-red-800", "Failed"
            | _ -> "bg-gray-100", "text-gray-600", "Unknown"

        _span [ _class_ $"px-2 py-1 text-xs font-medium rounded-full {bgClass} {textClass}" ] [ Text.raw label ]

    let private sideBadge (side: OrderSide) =
        let (bgClass, textClass, label) =
            match side with
            | OrderSide.Buy -> "bg-green-100", "text-green-800", "Buy"
            | OrderSide.Sell -> "bg-red-100", "text-red-800", "Sell"
            | _ -> "bg-gray-100", "text-gray-600", "Unknown"

        _span [ _class_ $"px-2 py-1 text-xs font-medium rounded-full {bgClass} {textClass}" ] [ Text.raw label ]

    let private formatDecimal (value: decimal option) =
        match value with
        | Some v -> v.ToString("N4")
        | None -> "-"

    let private formatPipelineId (pipelineId: int option) =
        match pipelineId with
        | Some id -> string id
        | None -> "-"

    let orderRow (order: OrderListItem) =
        _tr [ _id_ $"order-{order.Id}"; _class_ "hover:bg-gray-50" ] [
            _td [ _class_ "px-6 py-4 whitespace-nowrap text-sm text-gray-500" ] [ Text.raw (string order.Id) ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap text-sm text-gray-500" ] [
                Text.raw (formatPipelineId order.PipelineId)
            ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap" ] [
                _span [ _class_ "font-medium text-gray-900" ] [ Text.raw order.Symbol ]
            ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap" ] [ sideBadge order.Side ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap" ] [ statusBadge order.Status ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900" ] [
                Text.raw (order.Quantity.ToString("N4"))
            ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap text-right text-sm text-gray-500" ] [
                Text.raw (formatDecimal order.Price)
            ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap text-right text-sm text-gray-500" ] [
                Text.raw (formatDecimal order.Fee)
            ]
            _td [ _class_ "px-6 py-4 whitespace-nowrap text-sm text-gray-500" ] [
                Text.raw (order.CreatedAt.ToString("MMM dd, HH:mm"))
            ]
        ]

    let emptyState =
        _tr [] [
            _td [ Attr.create "colspan" "9"; _class_ "px-6 py-12 text-center" ] [
                _div [ _class_ "text-gray-400" ] [
                    _i [ _class_ "fas fa-receipt text-4xl mb-3" ] []
                    _p [ _class_ "text-lg font-medium" ] [ Text.raw "No orders found" ]
                    _p [ _class_ "text-sm" ] [ Text.raw "Orders will appear here when you start trading" ]
                ]
            ]
        ]

    let loadingState =
        _tr [] [
            _td [ Attr.create "colspan" "9"; _class_ "px-6 py-8 text-center text-gray-500" ] [
                _i [ _class_ "fas fa-spinner fa-spin text-2xl mb-2" ] []
                _p [] [ Text.raw "Loading orders..." ]
            ]
        ]

    let private paginationControls (data: OrdersGridData) =
        let totalPages = int (Math.Ceiling(float data.TotalCount / float data.PageSize))
        let hasPrev = data.Page > 1
        let hasNext = data.Page < totalPages
        let startRecord = if data.TotalCount = 0 then 0 else (data.Page - 1) * data.PageSize + 1
        let endRecord = min (data.Page * data.PageSize) data.TotalCount

        let enabledBtnClass = "bg-white text-gray-700 border border-gray-300 hover:bg-gray-50"
        let disabledBtnClass = "bg-gray-100 text-gray-400 cursor-not-allowed"
        let prevBtnClass = if hasPrev then enabledBtnClass else disabledBtnClass
        let nextBtnClass = if hasNext then enabledBtnClass else disabledBtnClass

        _div [ _class_ "flex items-center justify-between px-6 py-3 bg-gray-50 border-t border-gray-200" ] [
            _div [ _class_ "text-sm text-gray-700" ] [
                Text.raw $"Showing {startRecord} to {endRecord} of {data.TotalCount} orders"
            ]
            _div [ _class_ "flex gap-2" ] [
                _button [
                    _type_ "button"
                    _class_ $"px-3 py-1 text-sm font-medium rounded-md {prevBtnClass}"
                    if hasPrev then
                        Hx.get $"/orders/table?page={data.Page - 1}"
                        Hx.targetCss "#orders-table-container"
                        Hx.swapOuterHtml
                        Attr.create "hx-include" "#orders-filter-form form"
                    else
                        Attr.create "disabled" "disabled"
                ] [ Text.raw "Previous" ]
                _span [ _class_ "px-3 py-1 text-sm text-gray-700" ] [ Text.raw $"Page {data.Page} of {totalPages}" ]
                _button [
                    _type_ "button"
                    _class_ $"px-3 py-1 text-sm font-medium rounded-md {nextBtnClass}"
                    if hasNext then
                        Hx.get $"/orders/table?page={data.Page + 1}"
                        Hx.targetCss "#orders-table-container"
                        Hx.swapOuterHtml
                        Attr.create "hx-include" "#orders-filter-form form"
                    else
                        Attr.create "disabled" "disabled"
                ] [ Text.raw "Next" ]
            ]
        ]

    let tableBody (data: OrdersGridData) =
        let rows =
            match data.Orders with
            | [] -> [ emptyState ]
            | orders -> orders |> List.map orderRow

        _div [ _id_ "orders-table-container" ] [
            _div [ _class_ "overflow-x-auto" ] [
                _table [ _class_ "min-w-full divide-y divide-gray-200" ] [
                    tableHeader
                    _tbody [ _class_ "bg-white divide-y divide-gray-200" ] rows
                ]
            ]
            paginationControls data
        ]

    let private ordersTable =
        _div [ _class_ "card overflow-hidden" ] [
            _div [ _id_ "orders-table-container"; Hx.get "/orders/table"; Hx.trigger "load"; Hx.swapOuterHtml ] [
                _div [ _class_ "overflow-x-auto" ] [
                    _table [ _class_ "min-w-full divide-y divide-gray-200" ] [
                        tableHeader
                        _tbody [ _class_ "bg-white divide-y divide-gray-200" ] [ loadingState ]
                    ]
                ]
            ]
        ]

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
