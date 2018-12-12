using System;
using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Threading.Tasks;
using System.Reflection;

namespace Nest.Events.Listener
{
    public class AwsSmsSender: INestEventNotifier
    {
        private readonly AmazonSimpleNotificationServiceClient _snsClient;
        private readonly string _topicArn;

        public AwsSmsSender(string accessKeyId, string secretAccessKey, string regionEndpoint, string topicArn)
        {
            _snsClient = new AmazonSimpleNotificationServiceClient(accessKeyId, secretAccessKey, ParseEndpoint(regionEndpoint));
            _topicArn = topicArn;
        }

        public async Task SendNotificationAsync(string deviceName, string timestamp)
        {
            var message = $"{deviceName} saw a person @ {timestamp}";
            var result = await _snsClient.PublishAsync(new PublishRequest(_topicArn, message)).ConfigureAwait(false);
            Console.WriteLine($"Message published with result code: {result.HttpStatusCode}");
        }

        // dynamically load the public static AWS RegionEndpoint from configuration
        private static RegionEndpoint ParseEndpoint(string regionEndpoint)
        {
            var endpoint = typeof(RegionEndpoint);
            var field = endpoint.GetField(regionEndpoint, BindingFlags.Static | BindingFlags.Public);
            return field.GetValue(null) as RegionEndpoint;
        }
    }
}
