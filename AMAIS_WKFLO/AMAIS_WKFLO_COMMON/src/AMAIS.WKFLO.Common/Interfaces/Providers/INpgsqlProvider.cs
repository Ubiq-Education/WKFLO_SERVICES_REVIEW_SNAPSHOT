using System.Data;
using System.Data.Common;

namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface INpgsqlProvider
    {
        DbDataSource GetDataSource();

        /// <summary>
        /// Creates a connection to the PostgreSQL database
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Open database connection</returns>
        Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a SQL command and returns the number of rows affected
        /// </summary>
        /// <param name="sql">SQL command to execute</param>
        /// <param name="parameters">Parameters for the SQL command</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Number of rows affected</returns>
        Task<int> ExecuteAsync(string sql, object parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a SQL query and returns a list of results
        /// </summary>
        /// <typeparam name="T">Type to deserialize the results into</typeparam>
        /// <param name="sql">SQL query to execute</param>
        /// <param name="parameters">Parameters for the SQL query</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>List of query results</returns>
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a SQL query and returns the first result or default
        /// </summary>
        /// <typeparam name="T">Type to deserialize the result into</typeparam>
        /// <param name="sql">SQL query to execute</param>
        /// <param name="parameters">Parameters for the SQL query</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>First result or default value</returns>
        Task<T> QueryFirstOrDefaultAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a SQL query within a transaction
        /// </summary>
        /// <param name="action">Action to execute within the transaction</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Result of the action</returns>
        Task<T> ExecuteInTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the database connection is valid
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>True if connection is valid, false otherwise</returns>
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    }
}
