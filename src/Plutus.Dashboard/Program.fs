open Falco
open Falco.Routing
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpLogging
open Microsoft.Extensions.DependencyInjection
open Serilog
open System
open Plutus.App.Pages
open Plutus.Core

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
    [ get "/" (requireAuth Index.get); get "/system-status" (requireAuth SystemStatus.Handler.status) ]

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
        @ coverageHeatmap
        @ candlestickSync
    )
    .Run(Response.ofPlainText "Not found")
