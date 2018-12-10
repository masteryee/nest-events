using System;

namespace Nest.Events.Listener
{
    public class NestAuthToken
    {
        public string AccessToken { get; set; }
        public DateTime Expiration { get; set; }
    }
}
