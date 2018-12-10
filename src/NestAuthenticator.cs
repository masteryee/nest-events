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
        private readonly HttpClient _httpClient;
        private readonly HttpListener _httpListener = new HttpListener();
        private readonly Dictionary<string, string> _apiTokenFormParameters = new Dictionary<string, string>(4);
        private readonly string _clientId;
        private readonly string _redirectUrl;
        public const string TokenCacheLocation = "C:\\ProgramData\\Nest\\token.json";

        public NestAuthenticator(HttpClient httpClient, string clientId, string clientSecret, string redirectUrl)
        {
            _httpClient = httpClient;
            _clientId = clientId;
            _redirectUrl = redirectUrl;
            _apiTokenFormParameters["client_id"] = clientId;
            _apiTokenFormParameters["client_secret"] = clientSecret;
            _apiTokenFormParameters["grant_type"] = "authorization_code";
            _apiTokenFormParameters["code"] = null;
        }

        private void EnsureRedirectServerIsListening()
        {
            if (!_httpListener.IsListening)
            {
                _httpListener.Prefixes.Add(_redirectUrl);
                _httpListener.Start();
                Console.WriteLine($"Listening for requests on {_redirectUrl}");
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
                context = await _httpListener.GetContextAsync();
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
            Console.WriteLine($"Stopped listening for requests on {_redirectUrl}");
            return authenticationCode;
        }

        public async Task<string> GetApiTokenAsync(string authorizationCode)
        {
            _apiTokenFormParameters["code"] = authorizationCode;

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.home.nest.com/oauth2/access_token");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Content = new FormUrlEncodedContent(_apiTokenFormParameters);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

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
            await File.WriteAllTextAsync(TokenCacheLocation, tokenJson);
            return body.access_token;
        }
    }
}
