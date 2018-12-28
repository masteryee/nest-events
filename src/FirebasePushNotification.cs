using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Events.Listener
{
    public class FirebasePushNotification : INestEventNotifier
    {
        public Task SendNotificationAsync(string deviceName, string timestamp)
        {
            throw new NotImplementedException();
        }
    }
}
