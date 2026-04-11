open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Serilog

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

// CoreServices.register webapp.Services webapp.Configuration

let app = webapp.Build()

app.UseHttpsRedirection() |> ignore
app.UseRouting() |> ignore

app.UseFalco([]).Run(Response.ofPlainText "Not found")
