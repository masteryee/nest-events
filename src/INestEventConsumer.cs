using System.Threading.Tasks;

namespace Nest.Events.Listener
{
    public interface INestEventConsumer
    {
        Task ListenAsync();
    }
}
