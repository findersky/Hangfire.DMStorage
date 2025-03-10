using System;
using System.Threading;

using Dapper;
using Hangfire.Annotations;
using Hangfire.Logging;
using Hangfire.Server;

namespace Hangfire.DMStorage
{
#pragma warning disable 618
    internal class CountersAggregator : IBackgroundProcess,IServerComponent
#pragma warning restore 618
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(CountersAggregator));

        private const int NumberOfRecordsInSinglePass = 1000;
        private static readonly TimeSpan DelayBetweenPasses = TimeSpan.FromMilliseconds(500);

        private readonly DMStorage _storage;
        private readonly TimeSpan _interval;

        public CountersAggregator(DMStorage storage, TimeSpan interval)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _interval = interval;
        }

        public void Execute([NotNull] BackgroundProcessContext context)
        {
            Execute(context.StoppingToken);
        }

        public void Execute(CancellationToken cancellationToken)
        {
            Logger.DebugFormat("Aggregating records in 'Counter' table...");

            var removedCount = 0;

            do
            {
                _storage.UseConnection(connection =>
                {
                    removedCount = connection.Execute(GetMergeQuery(), new { COUNT = NumberOfRecordsInSinglePass });
                });

                if (removedCount >= NumberOfRecordsInSinglePass)
                {
                    cancellationToken.WaitHandle.WaitOne(DelayBetweenPasses);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            } while (removedCount >= NumberOfRecordsInSinglePass);

            cancellationToken.WaitHandle.WaitOne(_interval);
        }

        public override string ToString()
        {
            return GetType().ToString();
        }

        private static string GetMergeQuery()
        {
            return @"
BEGIN
    MERGE INTO ""AggregatedCounter"" AC
         USING (  SELECT ""Key"", SUM (""Value"") AS ""Value"", MAX(""ExpireAt"") AS ""ExpireAt""
                    FROM (SELECT ""Key"", ""Value"", ""ExpireAt""
                            FROM ""Counter""
                           WHERE ROWNUM <= :COUNT) TMP
                GROUP BY ""Key"") C
            ON (AC.""Key"" = C.""Key"")
    WHEN MATCHED
    THEN
       UPDATE SET ""Value"" = AC.""Value"" + C.""Value"", ""ExpireAt"" = GREATEST (AC.""ExpireAt"", C.""ExpireAt"")
    WHEN NOT MATCHED
    THEN
       INSERT     (""Id""
                  ,""Key""
                  ,""Value""
                  ,""ExpireAt"")
           VALUES (SEQUENCED.NEXTVAL
                  ,C.""Key""
                  ,C.""Value""
                  ,C.""ExpireAt"");

   DELETE FROM ""Counter""
    WHERE ROWNUM <= :COUNT;
END;
";
        }


    }
}
