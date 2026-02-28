namespace Plutus.App.Pages.ApiKeys

open System
open System.Data
open Falco
open Falco.Markup
open Falco.Htmx
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Domain
open Plutus.Core.Infrastructure
open Plutus.Core.Repositories

module View =

    let private keyRow (key: ApiKey) =
        _tr
            [ _class_ "border-b border-slate-100" ]
            [ _td [ _class_ "px-4 py-3 text-sm text-slate-900" ] [ Text.raw key.Name ]
              _td [ _class_ "px-4 py-3 text-sm text-slate-500 font-mono" ] [ Text.raw $"{key.KeyPrefix}..." ]
              _td
                  [ _class_ "px-4 py-3 text-sm" ]
                  [ if key.IsActive then
                        _span
                            [ _class_ "px-2 py-0.5 text-xs font-medium rounded-full bg-green-50 text-green-700" ]
                            [ Text.raw "Active" ]
                    else
                        _span
                            [ _class_ "px-2 py-0.5 text-xs font-medium rounded-full bg-slate-100 text-slate-500" ]
                            [ Text.raw "Revoked" ] ]
              _td
                  [ _class_ "px-4 py-3 text-sm text-slate-500" ]
                  [ Text.raw (
                        if Option.isSome key.LastUsed then key.LastUsed.Value.ToString("yyyy-MM-dd HH:mm") else "Never"
                    ) ]
              _td
                  [ _class_ "px-4 py-3 text-sm text-slate-500" ]
                  [ Text.raw (key.CreatedAt.ToString("yyyy-MM-dd HH:mm")) ]
              _td
                  [ _class_ "px-4 py-3" ]
                  [ if key.IsActive then
                        _button
                            [ _class_ "text-red-500 hover:text-red-700 text-sm"
                              Hx.delete $"/settings/api-keys/{key.Id}"
                              Hx.targetCss "#api-keys-table"
                              Hx.swapInnerHtml
                              Hx.confirm "Are you sure you want to revoke this API key?" ]
                            [ Text.raw "Revoke" ] ] ]

    let keysTable (keys: ApiKey list) =
        _div
            [ _id_ "api-keys-table" ]
            [ if keys.IsEmpty then
                  _p [ _class_ "text-sm text-slate-500 py-4" ] [ Text.raw "No API keys yet." ]
              else
                  _table
                      [ _class_ "w-full" ]
                      [ _thead
                            []
                            [ _tr
                                  [ _class_ "border-b border-slate-200" ]
                                  [ _th
                                        [ _class_ "px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase" ]
                                        [ Text.raw "Name" ]
                                    _th
                                        [ _class_ "px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase" ]
                                        [ Text.raw "Prefix" ]
                                    _th
                                        [ _class_ "px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase" ]
                                        [ Text.raw "Status" ]
                                    _th
                                        [ _class_ "px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase" ]
                                        [ Text.raw "Last Used" ]
                                    _th
                                        [ _class_ "px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase" ]
                                        [ Text.raw "Created" ]
                                    _th
                                        [ _class_ "px-4 py-2 text-left text-xs font-medium text-slate-500 uppercase" ]
                                        [ Text.raw "" ] ] ]
                        _tbody [] (keys |> List.map keyRow) ] ]

    let createModal =
        _div
            []
            [ _div
                  [ _class_ "fixed inset-0 bg-black bg-opacity-50 transition-opacity z-40"
                    Hx.get "/settings/api-keys"
                    Hx.targetCss "#api-keys-content"
                    Hx.swapInnerHtml ]
                  []
              _div
                  [ _class_ "fixed inset-0 z-50 flex items-center justify-center p-4" ]
                  [ _div
                        [ _class_ "bg-white rounded-xl shadow-lg border border-slate-200 w-full max-w-md p-6" ]
                        [ _h3 [ _class_ "text-lg font-semibold text-slate-900 mb-4" ] [ Text.raw "Create API Key" ]
                          _form
                              [ _class_ "space-y-4"
                                Hx.post "/settings/api-keys"
                                Hx.targetCss "#api-keys-content"
                                Hx.swapInnerHtml ]
                              [ _div
                                    []
                                    [ _label
                                          [ _class_ "block text-sm font-medium text-slate-700 mb-1" ]
                                          [ Text.raw "Name" ]
                                      _input
                                          [ _type_ "text"
                                            _name_ "name"
                                            _required_
                                            _placeholder_ "e.g. Claude Code, MCP Server"
                                            _class_
                                                "w-full px-3 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-1 focus:ring-slate-300" ] ]
                                _button
                                    [ _type_ "submit"
                                      _class_
                                          "w-full bg-slate-900 text-white py-2 px-4 rounded-md text-sm font-medium hover:bg-slate-800 transition-colors" ]
                                    [ Text.raw "Create Key" ] ] ] ] ]

    let keyCreated (rawKey: string) (keys: ApiKey list) =
        _div
            []
            [ _div
                  [ _class_ "mb-6 p-4 bg-green-50 border border-green-200 rounded-lg" ]
                  [ _h3 [ _class_ "text-sm font-semibold text-green-800 mb-2" ] [ Text.raw "API Key Created" ]
                    _p
                        [ _class_ "text-xs text-green-700 mb-3" ]
                        [ Text.raw "Copy this key now. It will not be shown again." ]
                    _div
                        [ _class_
                              "bg-white border border-green-300 rounded p-3 font-mono text-sm text-slate-900 break-all select-all" ]
                        [ Text.raw rawKey ] ]
              keysTable keys ]

    let settingsPage (content: XmlNode) =
        _html
            []
            [ _head
                  []
                  [ _meta [ _charset_ "utf-8" ]
                    _meta [ _name_ "viewport"; _content_ "width=device-width, initial-scale=1" ]
                    _title [] [ Text.raw "API Keys - Plutus" ]
                    _link
                        [ _href_ "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"
                          _rel_ "stylesheet" ]
                    _link [ _href_ "./styles.css"; _rel_ "stylesheet" ]
                    _link
                        [ _href_ "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css"
                          _rel_ "stylesheet" ]
                    _script [ _src_ HtmxScript.cdnSrc ] []
                    _script [ _src_ "https://cdn.tailwindcss.com" ] [] ]
              _body
                  [ _class_ "min-h-screen bg-white" ]
                  [ _header
                        [ _class_ "bg-white border-b border-slate-200" ]
                        [ _div
                              [ _class_ "max-w-[80%] mx-auto px-4 sm:px-6 lg:px-8" ]
                              [ _div
                                    [ _class_ "flex justify-between items-center h-16" ]
                                    [ _div
                                          [ _class_ "flex items-center space-x-8" ]
                                          [ _h1
                                                [ _class_ "text-sm font-semibold tracking-tight text-slate-900" ]
                                                [ _a [ _href_ "/" ] [ Text.raw "Plutus System" ] ]
                                            _nav
                                                [ _class_ "hidden md:flex space-x-2" ]
                                                [ _a
                                                      [ _href_ "/"
                                                        _class_
                                                            "text-slate-500 hover:bg-slate-100 px-2 py-1 rounded-md text-sm font-medium" ]
                                                      [ Text.raw "Dashboard" ]
                                                  _a
                                                      [ _href_ "/settings/api-keys"
                                                        _class_
                                                            "text-slate-900 bg-slate-100 px-2 py-1 rounded-md text-sm font-medium" ]
                                                      [ Text.raw "API Keys" ] ] ]
                                      _div
                                          [ _class_ "flex items-center space-x-4" ]
                                          [ _a
                                                [ _href_ "/logout"
                                                  _class_
                                                      "text-slate-400 hover:text-slate-600 text-sm flex items-center gap-1.5 transition-colors" ]
                                                [ _i [ _class_ "fas fa-sign-out-alt text-xs" ] []; Text.raw "Logout" ] ] ] ] ]
                    _div
                        [ _class_ "max-w-[80%] mx-auto px-4 sm:px-6 lg:px-8 py-6" ]
                        [ _div
                              [ _class_ "flex justify-between items-center mb-6" ]
                              [ _div
                                    []
                                    [ _h2 [ _class_ "text-lg font-semibold text-slate-900" ] [ Text.raw "API Keys" ]
                                      _p
                                          [ _class_ "text-slate-400 text-sm mt-1" ]
                                          [ Text.raw "Manage API keys for programmatic access" ] ]
                                _button
                                    [ _type_ "button"
                                      _class_
                                          "inline-flex items-center px-3 py-1.5 border border-slate-200 text-slate-700 hover:bg-slate-50 font-medium text-sm rounded-md transition-colors"
                                      Hx.get "/settings/api-keys/modal"
                                      Hx.targetCss "#api-keys-content"
                                      Hx.swapInnerHtml ]
                                    [ _i [ _class_ "fas fa-plus mr-2 text-slate-400" ] []; Text.raw "New Key" ] ]
                          _div [ _id_ "api-keys-content" ] [ content ] ] ] ]

module Handler =

    let list: HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                match! ApiKeyRepository.getAll db ctx.RequestAborted with
                | Ok keys ->
                    if ctx.Request.Headers.ContainsKey("HX-Request") then
                        return! Response.ofHtml (View.keysTable keys) ctx
                    else
                        return! Response.ofHtml (View.settingsPage (View.keysTable keys)) ctx
                | Error _ ->
                    return!
                        Response.ofHtml
                            (View.settingsPage (
                                _p [ _class_ "text-red-500 text-sm" ] [ Text.raw "Failed to load API keys" ]
                            ))
                            ctx
            }

    let createModal: HttpHandler = fun ctx -> Response.ofHtml View.createModal ctx

    let create: HttpHandler =
        fun ctx ->
            task {
                let! form = Request.getForm ctx
                let name = form.GetString("name", "")

                if String.IsNullOrWhiteSpace(name) then
                    return!
                        Response.ofHtml (_p [ _class_ "text-red-500 text-sm p-4" ] [ Text.raw "Name is required" ]) ctx
                else
                    let rawKey = Authentication.generateApiKey ()
                    let hash = Authentication.computeSha256 rawKey
                    let prefix = rawKey.Substring(0, min 12 rawKey.Length)

                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                    match! ApiKeyRepository.create db name hash prefix ctx.RequestAborted with
                    | Ok _ ->
                        match! ApiKeyRepository.getAll db ctx.RequestAborted with
                        | Ok keys -> return! Response.ofHtml (View.keyCreated rawKey keys) ctx
                        | Error _ -> return! Response.ofHtml (View.keyCreated rawKey []) ctx
                    | Error _ ->
                        return!
                            Response.ofHtml
                                (_p [ _class_ "text-red-500 text-sm p-4" ] [ Text.raw "Failed to create API key" ])
                                ctx
            }

    let revoke (id: int) : HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()

                let! _ = ApiKeyRepository.deactivate db id ctx.RequestAborted

                match! ApiKeyRepository.getAll db ctx.RequestAborted with
                | Ok keys -> return! Response.ofHtml (View.keysTable keys) ctx
                | Error _ -> return! Response.ofHtml (View.keysTable []) ctx
            }
