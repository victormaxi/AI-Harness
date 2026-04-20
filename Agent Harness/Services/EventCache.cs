using System.Collections.Concurrent;

namespace Agent_Harness.Services
{
    public class DetectedEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SourceId { get; set; } = string.Empty; // Holds the Graph Message ID
        public string Type { get; set; } = "email";
        public string Summary { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public interface IEventCache
    {
        void AddEvent(DetectedEvent evt);
        IEnumerable<DetectedEvent> GetRecentEvents();
    }

    public class MemoryEventCache : IEventCache
    {
        private readonly ConcurrentQueue<DetectedEvent> _events = new();
        private const int MaxEvents = 10;

        public void AddEvent(DetectedEvent evt)
        {
            _events.Enqueue(evt);
            while (_events.Count > MaxEvents)
            {
                _events.TryDequeue(out _);
            }
        }

        public IEnumerable<DetectedEvent> GetRecentEvents()
        {
            return _events.OrderByDescending(e => e.Timestamp);
        }
    }
}
