using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using Npgsql;

namespace CarCareTracker.External.Implementations
{
    public class PGDBHealthCheck: IDBHealthCheck
    {
        private NpgsqlDataSource pgDataSource;
        private readonly ILogger<PGDBHealthCheck> _logger;
        public PGDBHealthCheck(IConfiguration config, ILogger<PGDBHealthCheck> logger)
        {
            pgDataSource = NpgsqlDataSource.Create(config["POSTGRES_CONNECTION"] ?? string.Empty);
            _logger = logger;
        }
        public ServerHealthCheck GetDatabaseHealth()
        {
            try
            {
                string cmd = "SHOW server_version;";
                var result = string.Empty;
                using (var ctext = pgDataSource.CreateCommand(cmd))
                {
                    using (NpgsqlDataReader reader = ctext.ExecuteReader())
                        while (reader.Read())
                        {
                            result = reader["server_version"] as string;
                        }
                }
                if (!string.IsNullOrWhiteSpace(result))
                {
                    return new ServerHealthCheck { 
                        Name = "Database connections health check", 
                        Status = "pass",
                        Success = true
                    };
                } else
                {
                    return new ServerHealthCheck { Name = "Database connections health check" };
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return new ServerHealthCheck { Name = "Database connections health check" };
            }
        }
    }
}
