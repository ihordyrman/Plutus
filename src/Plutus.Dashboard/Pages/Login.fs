module Plutus.App.Pages.Login

open System
open System.Data
open System.Security.Claims
open Falco
open Falco.Markup
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.Extensions.DependencyInjection
open Plutus.Core.Infrastructure
open Plutus.Core.Repositories

module View =
    let private headContent =
        _head [] [
            _meta [ Attr.create "charset" "utf-8" ]
            _meta [ _name_ "viewport"; Attr.create "content" "width=device-width, initial-scale=1" ]
            _title [] [ Text.raw "Login - Plutus" ]
            _link [
                _href_ "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"
                _rel_ "stylesheet"
            ]
            _link [ _href_ "./styles.css"; _rel_ "stylesheet" ]
            _link [
                _href_ "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/7.0.1/css/all.min.css"
                _rel_ "stylesheet"
            ]
            _script [ _src_ "https://cdn.tailwindcss.com" ] []
        ]

    let private logo =
        _div [ _class_ "flex items-center justify-center mb-8" ] [
            _div [ _class_ "w-12 h-12 bg-gray-900 rounded-xl flex items-center justify-center" ] [
                _i [ _class_ "fas fa-chart-line text-white text-xl" ] []
            ]
        ]

    let private formField id label inputType placeholder iconClass =
        _div [ _class_ "mb-4" ] [
            _label [ _for_ id; _class_ "block text-sm font-medium text-gray-700 mb-1.5" ] [ Text.raw label ]
            _div [ _class_ "relative" ] [
                _div [ _class_ "absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none" ] [
                    _i [ _class_ $"fas {iconClass} text-gray-400 text-sm" ] []
                ]
                _input [
                    _type_ inputType
                    _id_ id
                    _name_ id
                    _placeholder_ placeholder
                    _required_
                    _class_ "w-full pl-10 pr-4 py-2.5 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-gray-900 focus:border-transparent transition-all"
                ]
            ]
        ]

    let private rememberMeCheckbox =
        _div [ _class_ "flex items-center mb-6" ] [
            _input [
                _type_ "checkbox"
                _id_ "rememberMe"
                _name_ "rememberMe"
                _class_ "w-4 h-4 text-gray-900 border-gray-300 rounded focus:ring-gray-900"
            ]
            _label [ _for_ "rememberMe"; _class_ "ml-2 text-sm text-gray-600" ] [ Text.raw "Remember me for 30 days" ]
        ]

    let private errorMessage (message: string option) =
        match message with
        | Some msg ->
            _div [ _class_ "mb-4 p-3 bg-red-50 border border-red-200 rounded-lg" ] [
                _p [ _class_ "text-sm text-red-600 flex items-center" ] [
                    _i [ _class_ "fas fa-exclamation-circle mr-2" ] []
                    Text.raw msg
                ]
            ]
        | None -> _div [] []

    let loginForm (error: string option) =
        _html [] [
            headContent
            _body [ _class_ "min-h-screen bg-gray-50 flex items-center justify-center p-4" ] [
                _div [ _class_ "w-full max-w-sm" ] [
                    logo
                    _div [ _class_ "bg-white rounded-xl shadow-sm border border-gray-100 p-8" ] [
                        _h1 [ _class_ "text-xl font-semibold text-gray-900 text-center mb-2" ] [ Text.raw "Welcome back" ]
                        _p [ _class_ "text-sm text-gray-500 text-center mb-6" ] [ Text.raw "Sign in to your account" ]
                        errorMessage error
                        _form [ _method_ "POST"; _action_ "/login" ] [
                            formField "username" "Username" "text" "Enter your username" "fa-user"
                            formField "password" "Password" "password" "Enter your password" "fa-lock"
                            rememberMeCheckbox
                            _button [
                                _type_ "submit"
                                _class_ "w-full bg-gray-900 text-white py-2.5 px-4 rounded-lg text-sm font-medium hover:bg-gray-800 transition-colors"
                            ] [ Text.raw "Sign in" ]
                        ]
                    ]
                    _p [ _class_ "text-xs text-gray-400 text-center mt-6" ] [ Text.raw "Plutus Trading System" ]
                ]
            ]
        ]

    let setupForm (error: string option) =
        _html [] [
            headContent
            _body [ _class_ "min-h-screen bg-gray-50 flex items-center justify-center p-4" ] [
                _div [ _class_ "w-full max-w-sm" ] [
                    logo
                    _div [ _class_ "bg-white rounded-xl shadow-sm border border-gray-100 p-8" ] [
                        _h1 [ _class_ "text-xl font-semibold text-gray-900 text-center mb-2" ] [ Text.raw "Create Admin Account" ]
                        _p [ _class_ "text-sm text-gray-500 text-center mb-6" ] [ Text.raw "Set up your first administrator account" ]
                        errorMessage error
                        _form [ _method_ "POST"; _action_ "/setup" ] [
                            formField "username" "Username" "text" "Choose a username" "fa-user"
                            formField "password" "Password" "password" "Choose a password" "fa-lock"
                            formField "confirmPassword" "Confirm Password" "password" "Confirm your password" "fa-check"
                            _button [
                                _type_ "submit"
                                _class_ "w-full bg-gray-900 text-white py-2.5 px-4 rounded-lg text-sm font-medium hover:bg-gray-800 transition-colors mt-2"
                            ] [ Text.raw "Create Account" ]
                        ]
                    ]
                    _p [ _class_ "text-xs text-gray-400 text-center mt-6" ] [ Text.raw "Plutus Trading System" ]
                ]
            ]
        ]

module Handler =
    let loginPage: HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! hasUsers = UserRepository.userExists db ctx.RequestAborted

                match hasUsers with
                | Ok true -> return! Response.ofHtml (View.loginForm None) ctx
                | Ok false -> return! Response.redirectTemporarily "/setup" ctx
                | Error _ -> return! Response.ofHtml (View.loginForm None) ctx
            }

    let loginPost: HttpHandler =
        fun ctx ->
            task {
                let! form = Request.getForm ctx
                let username = form.GetString("username", "")
                let password = form.GetString("password", "")
                let rememberMe = form.GetString("rememberMe", "") = "on"

                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! userResult = UserRepository.findByUsername db username ctx.RequestAborted

                match userResult with
                | Ok (Some user) when Authentication.verifyPassword password user.PasswordHash ->
                    let claims = [
                        Claim(ClaimTypes.Name, user.Username)
                        Claim(ClaimTypes.NameIdentifier, string user.Id)
                    ]
                    let identity = ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
                    let principal = ClaimsPrincipal(identity)

                    let authProperties = AuthenticationProperties()
                    authProperties.IsPersistent <- true
                    authProperties.ExpiresUtc <-
                        if rememberMe then
                            DateTimeOffset.UtcNow.Add(Authentication.extendedSessionDuration)
                        else
                            DateTimeOffset.UtcNow.Add(Authentication.defaultSessionDuration)

                    do! ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties)
                    return! Response.redirectTemporarily "/" ctx
                | _ ->
                    return! Response.ofHtml (View.loginForm (Some "Invalid username or password")) ctx
            }

    let setupPage: HttpHandler =
        fun ctx ->
            task {
                let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                use scope = scopeFactory.CreateScope()
                use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                let! hasUsers = UserRepository.userExists db ctx.RequestAborted

                match hasUsers with
                | Ok true -> return! Response.redirectTemporarily "/login" ctx
                | _ -> return! Response.ofHtml (View.setupForm None) ctx
            }

    let setupPost: HttpHandler =
        fun ctx ->
            task {
                let! form = Request.getForm ctx
                let username = form.GetString("username", "")
                let password = form.GetString("password", "")
                let confirmPassword = form.GetString("confirmPassword", "")

                if String.IsNullOrWhiteSpace(username) || username.Length < 3 then
                    return! Response.ofHtml (View.setupForm (Some "Username must be at least 3 characters")) ctx
                elif String.IsNullOrWhiteSpace(password) || password.Length < 6 then
                    return! Response.ofHtml (View.setupForm (Some "Password must be at least 6 characters")) ctx
                elif password <> confirmPassword then
                    return! Response.ofHtml (View.setupForm (Some "Passwords do not match")) ctx
                else
                    let scopeFactory = ctx.Plug<IServiceScopeFactory>()
                    use scope = scopeFactory.CreateScope()
                    use db = scope.ServiceProvider.GetRequiredService<IDbConnection>()
                    let! hasUsers = UserRepository.userExists db ctx.RequestAborted

                    match hasUsers with
                    | Ok true -> return! Response.redirectTemporarily "/login" ctx
                    | _ ->
                        let passwordHash = Authentication.hashPassword password
                        let! _ = UserRepository.create db username passwordHash ctx.RequestAborted
                        return! Response.redirectTemporarily "/login" ctx
            }

    let logout: HttpHandler =
        fun ctx ->
            task {
                do! ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                return! Response.redirectTemporarily "/login" ctx
            }
