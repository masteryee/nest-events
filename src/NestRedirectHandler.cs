using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nest.Events.Listener
{
    /// <summary>
    /// HTTP requests in .NET Core 2.1+ will drop authorization headers during automatic redirects.
    /// This handler will manually handle the redirection
    /// </summary>
    public class NestRedirectHandler: DelegatingHandler
    {
        public NestRedirectHandler()
        {
            InnerHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TemporaryRedirect)
            {
                var location = response.Headers.Location;

                if (location != null && IsHostTrusted(location))
                {
                    request.RequestUri = location;
                    response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                
            }
            return response;
        }

        // naive check if redirect host is trusted
        private bool IsHostTrusted(Uri uri)
        {
            return uri != null && 
                uri.Host.Contains(".nest.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
