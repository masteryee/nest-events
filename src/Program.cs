using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace Nest.Events.Listener
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();

            // There is a bug in v2.2.2 that prevents us from using the more straightforward .ConfigurePrimaryHttpMessageHandler<NestRedirectHandler>().
            // The next release will have the fix.
            services.AddTransient<NestRedirectHandler>();
            services
                .AddHttpClient("nest", h => h.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetService<NestRedirectHandler>());
            services.AddSingleton<HttpClient>(sp => sp.GetService<IHttpClientFactory>().CreateClient("nest"));

            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("ProductOAuth");
            var configuration = builder.Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<NestAuthenticator>();
            services.AddSingleton<INestEventConsumer, PersonDetector>();
            services.AddSingleton<AwsSmsSender>();
            services.AddSingleton<FirebasePushNotification>();
            services.AddSingleton<Func<string, INestEventNotifier>>(s => key =>
            {
                switch (key)
                {
                    case "aws": return s.GetService<AwsSmsSender>();
                    case "firebase": return s.GetService<FirebasePushNotification>();
                    default: throw new KeyNotFoundException();
                }
            });

            var container = services.BuildServiceProvider();

            Console.WriteLine("Welcome to the Nest Event streamer!");

            var personDetector = container.GetService<INestEventConsumer>();
            await personDetector.ListenAsync().ConfigureAwait(false);
        }
    }
}
