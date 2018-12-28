using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nest.Events.Listener
{
    public class NestEventNotifierFactory
    {
        public static INestEventNotifier GetAwsNotifier(IConfigurationRoot configuration)
        {
            return new AwsSmsSender(configuration["Aws:AccessKeyId"],
                configuration["Aws:SecretAccessKey"],
                configuration["Aws:RegionEndpoint"],
                configuration["Aws:SnsTopicArn"]);
        }

        public static INestEventNotifier GetFirebaseNotifier(IConfigurationRoot configuration)
        {
            return new FirebasePushNotification();
        }
    }
}
