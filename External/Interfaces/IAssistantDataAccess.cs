using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    /// <summary>Chat history of the assistant, private to each user.</summary>
    public interface IAssistantDataAccess
    {
        /// <summary>Stores a message and returns its id; 0 on failure.</summary>
        int Add(AssistantMessage message);
        /// <summary>Up to <paramref name="limit"/> messages of the user with an id below <paramref name="beforeId"/> (all when 0), newest first.</summary>
        List<AssistantMessage> GetPage(int userId, int beforeId, int limit);
        /// <summary>Removes the user's whole history. Returns how many messages were removed.</summary>
        int DeleteAll(int userId);
        /// <summary>Removes every user's messages older than the time. Returns how many were removed.</summary>
        int DeleteOlderThan(DateTime cutoffUtc);
    }
}
