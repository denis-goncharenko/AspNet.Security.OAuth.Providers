﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers
 * for more information concerning the license and the contributors participating to this project.
 */

using System.Net.Http.Headers;
using System.Net.Mime;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNet.Security.OAuth.Zoho;

public partial class ZohoAuthenticationHandler : OAuthHandler<ZohoAuthenticationOptions>
{
    public ZohoAuthenticationHandler(
        [NotNull] IOptionsMonitor<ZohoAuthenticationOptions> options,
        [NotNull] ILoggerFactory logger,
        [NotNull] UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        [NotNull] ClaimsIdentity identity,
        [NotNull] AuthenticationProperties properties,
        [NotNull] OAuthTokenResponse tokens)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        requestMessage.Version = Backchannel.DefaultRequestVersion;

        using var response = await Backchannel.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            await Log.UserProfileErrorAsync(Logger, response, Context.RequestAborted);
            throw new HttpRequestException("An error occurred while retrieving the user profile.");
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));

        var principal = new ClaimsPrincipal(identity);
        var context = new OAuthCreatingTicketContext(principal, properties, Context, Scheme, Options, Backchannel, tokens, payload.RootElement);
        context.RunClaimActions();

        await Events.CreatingTicket(context);
        return new AuthenticationTicket(context.Principal!, context.Properties, Scheme.Name);
    }

    protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        var location = Context.Request.Query["location"];

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, ZohoAuthenticationDefaults.ServerInfoEndpoint);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        requestMessage.Version = Backchannel.DefaultRequestVersion;

        using var response = await Backchannel.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            await Log.ServerInfoErrorAsync(Logger, response, Context.RequestAborted);
            throw new HttpRequestException("An error occurred while retrieving the server info.");
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));

        var domain = payload.RootElement.GetProperty("locations").GetProperty(location.ToString());
        Options.UserInformationEndpoint = $"{domain}{ZohoAuthenticationDefaults.UserInformationPath}";
        Options.TokenEndpoint = $"{domain}{ZohoAuthenticationDefaults.TokenPath}";

        return await base.HandleRemoteAuthenticateAsync();
    }

    private static partial class Log
    {
        internal static async Task UserProfileErrorAsync(ILogger logger, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            UserProfileError(
                logger,
                response.StatusCode,
                response.Headers.ToString(),
                await response.Content.ReadAsStringAsync(cancellationToken));
        }

        internal static async Task ServerInfoErrorAsync(ILogger logger, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            ServerInfoErrorAsync(
                logger,
                response.StatusCode,
                response.Headers.ToString(),
                await response.Content.ReadAsStringAsync(cancellationToken));
        }

        [LoggerMessage(1, LogLevel.Error, "An error occurred while retrieving the user profile: the remote server returned a {Status} response with the following payload: {Headers} {Body}.")]
        private static partial void UserProfileError(
            ILogger logger,
            System.Net.HttpStatusCode status,
            string headers,
            string body);

        [LoggerMessage(2, LogLevel.Error, "An error occurred while retrieving the server info: the remote server returned a {Status} response with the following payload: {Headers} {Body}.")]
        private static partial void ServerInfoErrorAsync(
            ILogger logger,
            System.Net.HttpStatusCode status,
            string headers,
            string body);
    }
}
