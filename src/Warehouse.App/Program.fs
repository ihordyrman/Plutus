open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.HttpLogging
open Microsoft.Extensions.DependencyInjection
open Serilog
open System
open Warehouse.App.Pages
open Warehouse.Core


let general = [ get "/" Index.get; get "/system-status" SystemStatus.Handler.status ]

let balances =
    [ get "/balance/total" Balance.Handler.total
      mapGet "/balance/{marketType:int}" _.GetInt("marketType") Balance.Handler.market ]

let markets = [ get "/markets/count" Markets.Handler.count; get "/markets/grid" Markets.Handler.grid ]

let accounts =
    [ get "/accounts/modal" CreateAccount.Handler.modal
      get "/accounts/modal/close" CreateAccount.Handler.closeModal
      post "/accounts/create" CreateAccount.Handler.create
      mapGet "/accounts/{id:int}/details/modal" _.GetInt("id") AccountDetails.Handler.modal
      mapGet "/accounts/{id:int}/edit/modal" _.GetInt("id") AccountEdit.Handler.modal
      mapPost "/accounts/{id:int}/edit" _.GetInt("id") AccountEdit.Handler.update
      mapDelete "/accounts/{id:int}" _.GetInt("id") AccountEdit.Handler.delete ]

let orders =
    [ get "/orders/count" Orders.Handler.count
      get "/orders/grid" Orders.Handler.grid
      get "/orders/table" Orders.Handler.table ]

let pipelines =
    [ get "/pipelines/count" Pipeline.Handler.count
      get "/pipelines/grid" Pipeline.Handler.grid
      get "/pipelines/table" Pipeline.Handler.table
      get "/pipelines/modal" CreatePipeline.Handler.modal
      get "/pipelines/modal/close" CreatePipeline.Handler.closeModal
      post "/pipelines/create" CreatePipeline.Handler.create
      mapGet "/pipelines/{id:int}/details/modal" _.GetInt("id") PipelineDetails.Handler.modal
      mapGet "/pipelines/{id:int}/edit/modal" _.GetInt("id") PipelineEdit.Handler.modal
      mapPost "/pipelines/{id:int}/edit" _.GetInt("id") PipelineEdit.Handler.update
      mapDelete "/pipelines/{id:int}" _.GetInt("id") Pipeline.Handler.delete
      mapGet "/pipelines/{id:int}/steps/list" _.GetInt("id") PipelineEdit.Handler.stepsList
      mapGet "/pipelines/{id:int}/steps/selector" _.GetInt("id") PipelineEdit.Handler.stepSelector
      mapPost "/pipelines/{id:int}/steps/add" _.GetInt("id") PipelineEdit.Handler.addStep

      get
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/editor"
          (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.stepEditor pipelineId stepId ctx
          )

      post
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/toggle"
          (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.toggleStep pipelineId stepId ctx
          )
      delete
          "/pipelines/{pipelineId:int}/steps/{stepId:int}"
          (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.deleteStep pipelineId stepId ctx
          )
      post
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/move"
          (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.moveStep pipelineId stepId ctx
          )
      post
          "/pipelines/{pipelineId:int}/steps/{stepId:int}/save"
          (fun ctx ->
              let route = Request.getRoute ctx
              let pipelineId = route.GetInt("pipelineId")
              let stepId = route.GetInt("stepId")
              PipelineEdit.Handler.saveStep pipelineId stepId ctx
          ) ]

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

webapp.Services.AddHttpLogging(
    Action<HttpLoggingOptions>(fun options -> options.LoggingFields <- HttpLoggingFields.All)
)
|> ignore

let app = webapp.Build()

app.UseHttpsRedirection() |> ignore
app.UseRouting() |> ignore
app.UseDefaultFiles().UseStaticFiles() |> ignore
app.UseFalco(general @ balances @ markets @ accounts @ pipelines @ orders).Run(Response.ofPlainText "Not found")
