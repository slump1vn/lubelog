using CarCareTracker.Logic;
using System.Collections.Concurrent;

namespace CarCareTracker.Helper
{
    /// <summary>
    /// A fixed one-minute window of assistant requests per signed-in user. The home AI answers one request at
    /// a time, so one user (or a stolen token) cannot keep it busy for everyone. Kept per user rather than per
    /// address because the whole family shares one address at home.
    /// </summary>
    public class AssistantThrottle
    {
        private readonly ConcurrentDictionary<int, (DateTime WindowStartUtc, int Count)> _windows = new ConcurrentDictionary<int, (DateTime, int)>();
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        /// <summary>True when the request may go ahead; counts it.</summary>
        public bool TryAcquire(int userId, DateTime nowUtc)
        {
            var allowed = true;
            _windows.AddOrUpdate(userId,
                _ => (nowUtc, 1),
                (_, current) =>
                {
                    if (nowUtc - current.WindowStartUtc >= Window)
                    {
                        return (nowUtc, 1);
                    }
                    if (current.Count >= AssistantRules.RequestsPerMinute)
                    {
                        allowed = false;
                        return current;
                    }
                    return (current.WindowStartUtc, current.Count + 1);
                });
            return allowed;
        }
    }
}
