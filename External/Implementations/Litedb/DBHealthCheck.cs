using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;

namespace CarCareTracker.External.Implementations
{
    public class DBHealthCheck : IDBHealthCheck
    {
        private ILiteDBHelper _liteDB { get; set; }
        private ILogger<DBHealthCheck> _logger { get; set; }
        public DBHealthCheck(ILiteDBHelper liteDB, ILogger<DBHealthCheck> logger)
        {
            _liteDB = liteDB;
            _logger = logger;
        }
        public ServerHealthCheck GetDatabaseHealth()
        {
            try
            {
                var db = _liteDB.GetLiteDB();
                return new ServerHealthCheck
                {
                    Name = "Database connections health check",
                    Status = "pass",
                    Success = true
                };
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return new ServerHealthCheck { Name = "Database connections health check" };
            }
        }
    }
}
