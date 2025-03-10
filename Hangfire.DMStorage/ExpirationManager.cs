using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;

using Dapper;

using Hangfire.Logging;
using Hangfire.Server;

namespace Hangfire.DMStorage
{
#pragma warning disable 618
    internal class ExpirationManager : IBackgroundProcess, IServerComponent
#pragma warning restore 618
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(ExpirationManager));

        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
        private const string DistributedLockKey = "expirationmanager";
        private static readonly TimeSpan DelayBetweenPasses = TimeSpan.FromSeconds(1);
        private const int NumberOfRecordsInSinglePass = 1000;

        private static readonly List<Tuple<string, bool>> TablesToProcess = new List<Tuple<string, bool>>
        {
            // This list must be sorted in dependency order 
            new Tuple<string, bool>("JobParameter", true),
            new Tuple<string, bool>("JobQueue", true),
            new Tuple<string, bool>("State", true),
            new Tuple<string, bool>("AggregatedCounter", false),
            new Tuple<string, bool>("List", false),
            new Tuple<string, bool>("Set", false),
            new Tuple<string, bool>("Hash", false),
            new Tuple<string, bool>("Job", false)
        };

        private readonly DMStorage _storage;
        private readonly TimeSpan _checkInterval;

        public ExpirationManager(DMStorage storage)
            : this(storage, TimeSpan.FromHours(1))
        {
        }

        public ExpirationManager(DMStorage storage, TimeSpan checkInterval)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _checkInterval = checkInterval;
        }

        public void Execute(BackgroundProcessContext context)
        {
            Execute(context.StoppingToken);
        }

        public void Execute(CancellationToken cancellationToken)
        {
            foreach (var tuple in TablesToProcess)
            {
                Logger.DebugFormat("Removing outdated records from table '{0}'...", tuple.Item1);

                var removedCount = 0;

                do
                {
                    _storage.UseConnection(connection =>
                    {
                        try
                        {
                            Logger.DebugFormat("Deleting records from table: {0}", tuple.Item1);

                            using (new DMDistributedLock(connection, DistributedLockKey, DefaultLockTimeout, cancellationToken).Acquire())
                            {
                                var query = $@"DELETE FROM ""{tuple.Item1}"" WHERE ""ExpireAt"" < :NOW AND ROWNUM <= :COUNT";
                                if (tuple.Item2)
                                {
                                    query = $@"DELETE FROM ""{tuple.Item1}"" WHERE ""JobId"" IN (SELECT ""Id"" FROM ""Job"" WHERE ""ExpireAt"" < :NOW AND ROWNUM <= :COUNT)";
                                }
                                removedCount = connection.Execute(query, new { NOW = DateTime.UtcNow, COUNT = NumberOfRecordsInSinglePass });
                            }

                            Logger.DebugFormat("removed records count={0}", removedCount);
                        }
                        catch (DbException ex)
                        {
                            Logger.Error(ex.ToString());
                        }
                    });

                    if (removedCount > 0)
                    {
                        Logger.Trace($"Removed {removedCount} outdated record(s) from '{tuple.Item1}' table.");

                        cancellationToken.WaitHandle.WaitOne(DelayBetweenPasses);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                } while (removedCount > 0);
            }

            cancellationToken.WaitHandle.WaitOne(_checkInterval);
        }

        public override string ToString()
        {
            return GetType().ToString();
        }
    }
}
