using System;
using System.Threading.Tasks;

namespace Nest.Events.Listener
{
    public interface INestEventNotifier
    {
        // sends SMS
        Task SendNotificationAsync(string deviceName, string timestamp);
    }
}