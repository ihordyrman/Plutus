open Falco
open Falco.Routing
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpLogging
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Infrastructure
open Serilog
open System
open Plutus.App.Pages
open Plutus.Core
open Plutus.Core.Queries
open Plutus.Dashboard.Api

let auth =
    [ get "/login" Login.Handler.loginPage
      post "/login" Login.Handler.loginPost
      get "/setup" Login.Handler.setupPage
      post "/setup" Login.Handler.setupPost
      get "/logout" Login.Handler.logout ]

let requireAuth (handler: HttpHandler) : HttpHandler =
    fun ctx ->
        task {
            if ctx.User.Identity.IsAuthenticated then
                return! handler ctx
            else
                return! Response.redirectTemporarily "/login" ctx
        }

let instruments =
    [ get "/instruments/base-currencies" (requireAuth Instruments.Handler.baseCurrencies)
      get "/instruments/quote-currencies" (requireAuth Instruments.Handler.quoteCurrencies) ]

let general =
    [ get "/" (requireAuth Index.get)
      get "/system-status" (requireAuth SystemStatus.Handler.status) ]

let balances =
    [ get "/balance/total" (requireAuth Balance.Handler.total)
      mapGet
          "/balance/{marketType:int}"
          _.GetInt("marketType")
          (fun marketType -> requireAuth (Balance.Handler.market marketType)) ]

let markets =
    [ get "/markets/count" (requireAuth Markets.Handler.count)
      get "/markets/grid" (requireAuth Markets.Handler.grid) ]

let accounts =
    [ get "/accounts/modal" (requireAuth CreateAccount.Handler.modal)
      get "/accounts/modal/close" (requireAuth CreateAccount.Handler.closeModal)
      post "/accounts/create" (requireAuth CreateAccount.Handler.create)
      mapGet "/accounts/{id:int}/details/modal" _.GetInt("id") (fun id -> requireAuth (AccountDetails.Handler.modal id))
      mapGet "/accounts/{id:int}/edit/modal" _.GetInt("id") (fun id -> requireAuth (AccountEdit.Handler.modal id))
      mapPost "/accounts/{id:int}/edit" _.GetInt("id") (fun id -> requireAuth (AccountEdit.Handler.update id))
      mapDelete "/accounts/{id:int}" _.GetInt("id") (fun id -> requireAuth (AccountEdit.Handler.delete id)) ]

let orders =
    [ get "/orders/count" (requireAuth Orders.Handler.count)
      get "/orders/grid" (requireAuth Orders.Handler.grid)
      get "/orders/table" (requireAuth Orders.Handler.table) ]

let pipelines =
    [ get "/pipelines/count" (requireAuth Pipeline.Handler.count)
      get "/pipelines/grid" (requireAuth Pipeline.Handler.grid)
      get "/pipelines/table" (requireAuth Pipeline.Handler.table)
      get "/pipelines/modal" (requireAuth CreatePipeline.Handler.modal)
      get "/pipelines/modal/close" (requireAuth CreatePipeline.Handler.closeModal)
      post "/pipelines/create" (requireAuth CreatePipeline.Handler.create)
      mapGet
          "/pipelines/{id:int}/details/modal"
          _.GetInt("id")
          (fun id -> requireAuth (PipelineDetails.Handler.modal id))
      mapGet "/pipelines/{id:int}/edit/modal" _.GetInt("id") (fun id -> requireAuth (PipelineEdit.Handler.modal id))
      mapPost "/pipelines/{id:int}/edit" _.GetInt("id") (fun id -> requireAuth (PipelineEdit.Handler.update id))
      mapDelete "/pipelines/{id:int}" _.GetInt("id") (fun id -> requireAuth (Pipeline.Handler.delete id))
      mapGet
          "/pipelines/{id:int}/traces/modal"
          _.GetInt("id")
          (fun id -> requireAuth (PipelineTraces.Handler.tracesModal id))
      mapGet
          "/pipelines/{id:int}/traces/list"
          _.GetInt("id")
          (fun id -> requireAuth (PipelineTraces.Handler.tracesList id))
      get
          "/pipelines/{pipelineId:int}/traces/{executionId}"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let executionId = route.GetString("executionId")
              PipelineTraces.Handler.executionDetail pipelineId executionId ctx
          ))
      mapGet "/pipelines/{id:int}/steps/list" _.GetInt("id") (fun id -> requireAuth (PipelineEdit.Handler.stepsList id))
      mapGet
          "/pipelines/{id:int}/steps/selector"
          _.GetInt("id")
          (fun id -> requireAuth (PipelineEdit.Handler.stepSelector id))
      mapPost "/pipelines/{id:int}/steps/add" _.GetInt("id") (fun id -> requireAuth (PipelineEdit.Handler.addStep id))

      get
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/editor"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.stepEditor pipelineId stepId ctx
          ))

      post
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/toggle"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.toggleStep pipelineId stepId ctx
          ))
      delete
          "/pipelines/{pipelineId:int}/steps/{stepId:int}"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.deleteStep pipelineId stepId ctx
          ))
      post
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/move"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.moveStep pipelineId stepId ctx
          ))
      post
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/save"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.saveStep pipelineId stepId ctx
          )) ]

let backtests =
    [ get "/backtests/grid" (requireAuth Backtest.Handler.grid)
      get "/backtests/count" (requireAuth Backtest.Handler.count)
      mapGet "/backtests/{id:int}/row" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.row id))
      mapDelete "/backtests/{id:int}" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.delete id))
      mapGet
          "/backtests/configure/{pipelineId:int}"
          _.GetInt("pipelineId")
          (fun id -> requireAuth (Backtest.Handler.configureModal id))
      get "/backtests/modal/close" (requireAuth Backtest.Handler.closeModal)
      post "/backtests/run" (requireAuth Backtest.Handler.run)
      mapGet "/backtests/{id:int}/status" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.status id))
      mapGet "/backtests/{id:int}/results" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.results id))
      mapGet "/backtests/{id:int}/trades" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.trades id))
      mapGet "/backtests/{id:int}/executions" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.executions id))
      get
          "/backtests/{runId:int}/traces/{executionId}"
          (requireAuth (fun ctx ->
              let route = Request.getRoute ctx
              Backtest.Handler.traces (route.GetInt "runId") (route.GetString "executionId") ctx
          ))
      mapGet "/backtests/{id:int}/chart-data" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.chartData id))
      mapGet
          "/backtests/{id:int}/traces/by-time"
          _.GetInt("id")
          (fun id -> requireAuth (Backtest.Handler.tracesByTime id))
      mapGet "/backtests/{id:int}/export" _.GetInt("id") (fun id -> requireAuth (Backtest.Handler.export id)) ]

let coverageHeatmap =
    [ get "/coverage-heatmap" (requireAuth CoverageHeatmap.Handler.heatmap)
      delete "/coverage-heatmap/{symbol}" (requireAuth CoverageHeatmap.Handler.deleteSymbol) ]

let candlestickSync =
    [ get "/candlestick-sync/modal" (requireAuth CandlestickSync.Handler.modal)
      get "/candlestick-sync/modal/close" (requireAuth CandlestickSync.Handler.closeModal)
      post "/candlestick-sync/start" (requireAuth CandlestickSync.Handler.start)
      get "/candlestick-sync/jobs" (requireAuth CandlestickSync.Handler.jobs)
      mapPost
          "/candlestick-sync/jobs/{jobId:int}/pause"
          _.GetInt("jobId")
          (fun jobId -> requireAuth (CandlestickSync.Handler.pause jobId))
      mapPost
          "/candlestick-sync/jobs/{jobId:int}/resume"
          _.GetInt("jobId")
          (fun jobId -> requireAuth (CandlestickSync.Handler.resume jobId))
      mapPost
          "/candlestick-sync/jobs/{jobId:int}/stop"
          _.GetInt("jobId")
          (fun jobId -> requireAuth (CandlestickSync.Handler.stop jobId))
      mapDelete
          "/candlestick-sync/jobs/{jobId:int}"
          _.GetInt("jobId")
          (fun jobId -> requireAuth (CandlestickSync.Handler.remove jobId)) ]

let apiKeys =
    [ get "/settings/api-keys" (requireAuth ApiKeys.Handler.list)
      get "/settings/api-keys/modal" (requireAuth ApiKeys.Handler.createModal)
      post "/settings/api-keys" (requireAuth ApiKeys.Handler.create)
      mapDelete "/settings/api-keys/{id:int}" _.GetInt("id") (fun id -> requireAuth (ApiKeys.Handler.revoke id)) ]

let api =
    [ get "/api/v1/steps" (ApiAuth.requireApiKey StepsApi.list)
      get
          "/api/v1/steps/{key}"
          (ApiAuth.requireApiKey (fun ctx -> StepsApi.byKey (Request.getRoute(ctx).GetString("key")) ctx))
      get "/api/v1/pipelines" (ApiAuth.requireApiKey PipelinesApi.list)
      post "/api/v1/pipelines" (ApiAuth.requireApiKey PipelinesApi.create)
      mapGet "/api/v1/pipelines/{id:int}" _.GetInt("id") (fun id -> ApiAuth.requireApiKey (PipelinesApi.get id))
      put
          "/api/v1/pipelines/{id:int}"
          (ApiAuth.requireApiKey (fun ctx -> PipelinesApi.update (Request.getRoute(ctx).GetInt("id")) ctx))
      mapDelete "/api/v1/pipelines/{id:int}" _.GetInt("id") (fun id -> ApiAuth.requireApiKey (PipelinesApi.delete id))
      mapGet
          "/api/v1/pipelines/{id:int}/steps"
          _.GetInt("id")
          (fun id -> ApiAuth.requireApiKey (PipelineStepsApi.list id))
      post
          "/api/v1/pipelines/{id:int}/steps"
          (ApiAuth.requireApiKey (fun ctx -> PipelineStepsApi.add (Request.getRoute(ctx).GetInt("id")) ctx))
      put
          "/api/v1/pipelines/{pipelineId:int}/steps/{stepId:int}"
          (ApiAuth.requireApiKey (fun ctx ->
              let route = Request.getRoute ctx
              PipelineStepsApi.update (route.GetInt("pipelineId")) (route.GetInt("stepId")) ctx
          ))
      delete
          "/api/v1/pipelines/{pipelineId:int}/steps/{stepId:int}"
          (ApiAuth.requireApiKey (fun ctx ->
              let route = Request.getRoute ctx
              PipelineStepsApi.delete (route.GetInt("pipelineId")) (route.GetInt("stepId")) ctx
          ))
      put
          "/api/v1/pipelines/{id:int}/steps/bulk"
          (ApiAuth.requireApiKey (fun ctx -> PipelineStepsApi.bulk (Request.getRoute(ctx).GetInt("id")) ctx))
      post "/api/v1/backtests" (ApiAuth.requireApiKey BacktestsApi.run)
      get "/api/v1/backtests" (ApiAuth.requireApiKey BacktestsApi.list)
      mapGet "/api/v1/backtests/{id:int}" _.GetInt("id") (fun id -> ApiAuth.requireApiKey (BacktestsApi.get id))
      mapGet
          "/api/v1/backtests/{id:int}/trades"
          _.GetInt("id")
          (fun id -> ApiAuth.requireApiKey (BacktestsApi.trades id)) ]

let webapp = WebApplication.CreateBuilder()

webapp.Host.UseSerilog(fun context services configuration ->
    configuration.ReadFrom
        .Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
    |> ignore
)
|> ignore

CoreServices.register webapp.Services webapp.Configuration

webapp.Services.AddSingleton<CacheRefresher list>([ CoverageHeatmapCache.refresher ])
|> ignore

webapp.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(fun options ->
        options.LoginPath <- PathString("/login")
        options.LogoutPath <- PathString("/logout")
        options.Cookie.Name <- ".Plutus.Auth"
        options.Cookie.HttpOnly <- true
        options.Cookie.SecurePolicy <- CookieSecurePolicy.SameAsRequest
        options.Cookie.SameSite <- SameSiteMode.Lax
        options.ExpireTimeSpan <- TimeSpan.FromDays(30.0)
        options.SlidingExpiration <- true
    )
|> ignore

webapp.Services.AddAuthorization() |> ignore

webapp.Services.AddHttpLogging(
    Action<HttpLoggingOptions>(fun options -> options.LoggingFields <- HttpLoggingFields.All)
)
|> ignore

let app = webapp.Build()

app.UseHttpsRedirection() |> ignore
app.UseRouting() |> ignore
app.UseAuthentication() |> ignore
app.UseAuthorization() |> ignore
app.UseDefaultFiles().UseStaticFiles() |> ignore

app
    .UseFalco(
        auth
        @ general
        @ instruments
        @ balances
        @ markets
        @ accounts
        @ pipelines
        @ orders
        @ backtests
        @ coverageHeatmap
        @ candlestickSync
        @ apiKeys
        @ api
    )
    .Run(Response.ofPlainText "Not found")
