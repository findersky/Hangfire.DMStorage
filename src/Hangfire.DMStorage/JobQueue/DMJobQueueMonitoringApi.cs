using System;
using System.Collections.Generic;
using System.Linq;

using Dapper;

namespace Hangfire.DMStorage.JobQueue
{
    internal class DMJobQueueMonitoringApi : IPersistentJobQueueMonitoringApi
    {
        private static readonly TimeSpan QueuesCacheTimeout = TimeSpan.FromSeconds(5);
        private readonly object _cacheLock = new object();
        private List<string> _queuesCache = new List<string>();
        private DateTime _cacheUpdated;

        private readonly DMStorage _storage;
        public DMJobQueueMonitoringApi(DMStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public IEnumerable<string> GetQueues()
        {
            lock (_cacheLock)
            {
                if (_queuesCache.Count == 0 || _cacheUpdated.Add(QueuesCacheTimeout) < DateTime.UtcNow)
                {
                    var result = _storage.UseConnection(connection =>
                    {
                        return connection.Query(@"SELECT DISTINCT(""Queue"") FROM ""JobQueue""").Select(x => (string)x.Queue).ToList();
                    });

                    _queuesCache = result;
                    _cacheUpdated = DateTime.UtcNow;
                }

                return _queuesCache.ToList();
            }
        }

        public IEnumerable<int> GetEnqueuedJobIds(string queue, int from, int perPage)
        {
            const string sqlQuery = @"
SELECT ""JobId""
  FROM (SELECT ""JobId"", RANK () OVER (ORDER BY ""Id"") AS RANK
          FROM ""JobQueue""
         WHERE ""Queue"" = :QUEUE)
 WHERE RANK BETWEEN :S AND :E
";

            return _storage.UseConnection(connection =>
                connection.Query<int>(sqlQuery, new { QUEUE = queue, S = from + 1, E = from + perPage }));
        }

        public IEnumerable<int> GetFetchedJobIds(string queue, int from, int perPage)
        {
            return Enumerable.Empty<int>();
        }

        public EnqueuedAndFetchedCountDto GetEnqueuedAndFetchedCount(string queue)
        {
            return _storage.UseConnection(connection =>
            {
                var result = connection.QuerySingle<int>(@"SELECT COUNT(""Id"") FROM ""JobQueue"" WHERE ""Queue"" = :QUEUE", new { QUEUE = queue });

                return new EnqueuedAndFetchedCountDto
                {
                    EnqueuedCount = result
                };
            });
        }
    }
}