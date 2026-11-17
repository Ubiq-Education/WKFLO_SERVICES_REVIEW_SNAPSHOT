using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Settings;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using System.Data;
using System.Data.Common;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    /// <summary>
    /// Implementation of INpgsqlProvider for PostgreSQL database operations
    /// </summary>
    public class NpgsqlProvider : INpgsqlProvider
    {
        private readonly string _connectionString;
        private readonly int _commandTimeout;
        private readonly ILogger<NpgsqlProvider> _logger;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the NpgsqlProvider
        /// </summary>
        /// <param name="options">PostgreSQL configuration options</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <exception cref="ArgumentException">Thrown when connection string is not configured</exception>
        public NpgsqlProvider(IOptions<NpgsqlSettings> options, ILogger<NpgsqlProvider> logger)
        {
            var settings = options.Value;

            if (string.IsNullOrEmpty(settings.ConnectionString))
            {
                throw new ArgumentException("PostgreSQL connection string is not configured");
            }

            _connectionString = settings.ConnectionString;
            _commandTimeout = settings.CommandTimeout;
            _logger = logger;

            // Configure retry policy for transient errors
            _retryPolicy = Policy
                .Handle<NpgsqlException>(ex => IsTransientError(ex))
                .WaitAndRetryAsync(
                    settings.MaxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "Error executing PostgreSQL command (Attempt {RetryCount}/{MaxRetries}). Waiting {RetrySeconds} seconds before retrying.",
                            retryCount,
                            settings.MaxRetries,
                            timeSpan.TotalSeconds);
                    });

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
            dataSourceBuilder.UseVector();

            // The token is cached until shortly before its own expiry rather than for a fixed interval: the
            // credential can return an already-cached token with little lifetime left (BUG 7880).
            var tokenProvider = new PostgresEntraTokenProvider(AzureCredentialHelper.Create(), TimeProvider.System, _logger);
            dataSourceBuilder.UsePasswordProvider(
                passwordProvider: _ => tokenProvider.GetPassword(),
                passwordProviderAsync: (_, ct) => tokenProvider.GetPasswordAsync(ct));

            _dataSource = dataSourceBuilder.Build();
        }

        /// <inheritdoc/>
        public async Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _dataSource.OpenConnectionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create PostgreSQL connection");
                throw;
            }
        }

        public DbDataSource GetDataSource()
        {
            return _dataSource;
        }

        /// <inheritdoc/>
        public async Task<int> ExecuteAsync(string sql, object parameters = null, CancellationToken cancellationToken = default)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    using var connection = await CreateConnectionAsync(cancellationToken);
                    return await connection.ExecuteAsync(
                        new CommandDefinition(
                            sql,
                            parameters,
                            commandTimeout: _commandTimeout,
                            cancellationToken: cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing PostgreSQL command: {Sql}", sql);
                    throw;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    using var connection = await CreateConnectionAsync(cancellationToken);
                    return await connection.QueryAsync<T>(
                        new CommandDefinition(
                            sql,
                            parameters,
                            commandTimeout: _commandTimeout,
                            cancellationToken: cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing PostgreSQL query: {Sql}", sql);
                    throw;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    using var connection = await CreateConnectionAsync(cancellationToken);
                    return await connection.QueryFirstOrDefaultAsync<T>(
                        new CommandDefinition(
                            sql,
                            parameters,
                            commandTimeout: _commandTimeout,
                            cancellationToken: cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing PostgreSQL query for single result: {Sql}", sql);
                    throw;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<T> ExecuteInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> action, CancellationToken cancellationToken = default)
        {
            using var connection = await CreateConnectionAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            try
            {
                var result = await action(connection, transaction);
                transaction.Commit();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing PostgreSQL transaction");
                transaction.Rollback();
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = await CreateConnectionAsync(cancellationToken);
                await connection.ExecuteScalarAsync<int>("SELECT 1", commandTimeout: _commandTimeout);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostgreSQL connection test failed");
                return false;
            }
        }

        /// <summary>
        /// Determines if a PostgreSQL exception is transient and should be retried
        /// </summary>
        /// <param name="ex">The exception to check</param>
        /// <returns>True if the error is transient, false otherwise</returns>
        private bool IsTransientError(NpgsqlException ex)
        {
            // Common transient error codes in PostgreSQL
            // 08000: Connection exception
            // 08003: Connection does not exist
            // 08006: Connection failure
            // 08001: SQL client unable to establish SQL connection
            // 08004: SQL server rejected establishment of SQL connection
            // 57P01: Admin shutdown
            // 57P02: Crash shutdown
            // 57P03: Cannot connect now
            // 40001: Serialization failure (deadlock)
            // 40P01: Deadlock detected

            var transientErrorCodes = new[]
            {
                "08000", "08003", "08006", "08001", "08004",
                "57P01", "57P02", "57P03",
                "40001", "40P01"
            };

            if (ex.InnerException is PostgresException pgEx)
            {
                return Array.Exists(transientErrorCodes, code => code == pgEx.SqlState);
            }

            return false;
        }
    }
}
