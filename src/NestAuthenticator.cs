using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Nest.Events.Listener
{
    public class NestAuthenticator
    {
        private const string RedirectUrl = "http://localhost:9999/";
        private const string TokenCacheLocation = "C:\\ProgramData\\Nest\\token.json";

        private readonly HttpClient _httpClient;
        private readonly HttpListener _httpListener = new HttpListener();
        private readonly Dictionary<string, string> _apiTokenFormParameters = new Dictionary<string, string>(4);
        private readonly string _clientId;
        
        public NestAuthenticator(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _clientId = configuration["Nest:ClientId"];
            _apiTokenFormParameters["client_id"] = _clientId;
            _apiTokenFormParameters["client_secret"] = configuration["Nest:ClientSecret"];
            _apiTokenFormParameters["grant_type"] = "authorization_code";
            _apiTokenFormParameters["code"] = null;
        }

        private void EnsureRedirectServerIsListening()
        {
            if (!_httpListener.IsListening)
            {
                _httpListener.Prefixes.Add(RedirectUrl);
                _httpListener.Start();
                Console.WriteLine($"Listening for requests on {RedirectUrl}");
            }
        }

        public async Task<string> ReceiveAuthorizationCodeAsync()
        {
            EnsureRedirectServerIsListening();

            var csrfToken = Guid.NewGuid().ToString("N");
            var startInfo = new ProcessStartInfo()
            {
                FileName = $"https://home.nest.com/login/oauth2?client_id={_clientId}&state={csrfToken}",
                UseShellExecute = true,
            };

            HttpListenerContext context = null;
            using (var process = Process.Start(startInfo))
            {
                context = await _httpListener.GetContextAsync().ConfigureAwait(false);
            }

            var verifyState = context.Request.QueryString["state"];
            if (verifyState != csrfToken)
            {
                context.Response.Close();
                _httpListener.Stop();
                throw new InvalidOperationException("Anti-forgery token mismatch.");
            }

            var authenticationCode = context.Request.QueryString["code"];
            context.Response.Close();
            _httpListener.Stop();
            Console.WriteLine($"Stopped listening for requests on {RedirectUrl}");
            return authenticationCode;
        }

        public async Task<string> GetApiTokenAsync(string authorizationCode)
        {
            _apiTokenFormParameters["code"] = authorizationCode;

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.home.nest.com/oauth2/access_token");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Content = new FormUrlEncodedContent(_apiTokenFormParameters);

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var anonResponseType = new
            {
                access_token = "",
                expires_in = 0L
            };

            var body = JsonConvert.DeserializeAnonymousType(json, anonResponseType);
            var authTokenToSave = new NestAuthToken
            {
                AccessToken = body.access_token,
                Expiration = DateTime.UtcNow.AddSeconds(body.expires_in)
            };

            var tokenJson = JsonConvert.SerializeObject(authTokenToSave);
            await File.WriteAllTextAsync(TokenCacheLocation, tokenJson).ConfigureAwait(false);
            return body.access_token;
        }

        public async Task<string> CheckForExistingTokenAsync()
        {
            if (!File.Exists(TokenCacheLocation)) return null;

            var json = await File.ReadAllTextAsync(TokenCacheLocation);
            var existingToken = JsonConvert.DeserializeObject<NestAuthToken>(json);

            if (existingToken.Expiration < DateTime.UtcNow) return null;

            return existingToken.AccessToken;
        }
    }
}
