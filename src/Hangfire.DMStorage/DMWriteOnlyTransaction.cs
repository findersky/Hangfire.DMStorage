using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.DMStorage.Entities;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.DMStorage.CommonExtension;

namespace Hangfire.DMStorage
{
    internal class DMWriteOnlyTransaction : JobStorageTransaction
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(DMWriteOnlyTransaction));

        private readonly DMStorage _storage;

        private readonly Queue<Action<IDbConnection>> _commandQueue = new Queue<Action<IDbConnection>>();

        public DMWriteOnlyTransaction(DMStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public override void ExpireJob(string jobId, TimeSpan expireIn)
        {
            Logger.TraceFormat("ExpireJob jobId={0}", jobId);

            AcquireJobLock();

            QueueCommand(x =>
                x.Execute(
                    @"UPDATE ""Job"" SET ""ExpireAt"" = :EXPIRE_AT WHERE ""Id"" = :ID",
                    new { EXPIRE_AT = DateTime.UtcNow.Add(expireIn), ID = jobId }));
        }

        public override void PersistJob(string jobId)
        {
            Logger.TraceFormat("PersistJob jobId={0}", jobId);

            AcquireJobLock();

            QueueCommand(x => x.Execute(@"UPDATE ""Job"" SET ""ExpireAt"" = NULL WHERE ""Id"" = :ID", new { ID = jobId }));
        }

        public override void SetJobState(string jobId, IState state)
        {
            Logger.TraceFormat("SetJobState jobId={0}", jobId);

            AcquireStateLock();
            AcquireJobLock();

            var stateId = _storage.UseConnection(connection => connection.GetNextId());

            var dmDynamicParameters = new DynamicParameters();
            dmDynamicParameters.AddDynamicParams(new
            {
                STATE_ID = stateId,
                JOB_ID = jobId,
                NAME = state.Name,
                REASON = state.Reason,
                CREATED_AT = DateTime.UtcNow,
                ID = jobId
            });
            dmDynamicParameters.Add("DATA", SerializationHelper.Serialize(state.SerializeData(), SerializationOption.User),DbType.String, ParameterDirection.Input);

            QueueCommand(x => x.Execute(
                @"
BEGIN
 INSERT INTO ""State"" (""Id"", ""JobId"", ""Name"", ""Reason"", ""CreatedAt"", ""Data"")
      VALUES (:STATE_ID, :JOB_ID, :NAME, :REASON, :CREATED_AT, :DATA);
 
      UPDATE ""Job"" SET ""StateId"" = :STATE_ID, ""StateName"" = :NAME WHERE ""Id"" = :ID;
END;
",
                dmDynamicParameters));
        }

        public override void AddJobState(string jobId, IState state)
        {
            Logger.TraceFormat("AddJobState jobId={0}, state={1}", jobId, state);

            AcquireStateLock();

            var dmDynamicParameters = new DynamicParameters();
            dmDynamicParameters.AddDynamicParams(new
            {
                JOB_ID = jobId,
                NAME = state.Name,
                REASON = state.Reason,
                CREATED_AT = DateTime.UtcNow
            });
            dmDynamicParameters.Add("DATA", SerializationHelper.Serialize(state.SerializeData(), SerializationOption.User), DbType.String, ParameterDirection.Input);

            QueueCommand(x => x.Execute(
                @"INSERT INTO ""State"" (""Id"", ""JobId"", ""Name"", ""Reason"", ""CreatedAt"", ""Data"")  
                VALUES (SEQUENCED.NEXTVAL, :JOB_ID, :NAME, :REASON, :CREATED_AT,:DATA)", dmDynamicParameters));
        }

        public override void AddToQueue(string queue, string jobId)
        {
            Logger.TraceFormat("AddToQueue jobId={0}", jobId);

            var provider = _storage.QueueProviders.GetProvider(queue);
            var persistentQueue = provider.GetJobQueue();

            QueueCommand(x => persistentQueue.Enqueue(x, queue, jobId));
        }

        public override void IncrementCounter(string key)
        {
            Logger.TraceFormat("IncrementCounter key={0}", key);

            AcquireCounterLock();

            QueueCommand(x => x.Execute(@"INSERT INTO ""Counter"" (""Id"", ""Key"",""Value"") VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE)",
                    new { KEY = key, VALUE = +1 }));
        }


        public override void IncrementCounter(string key, TimeSpan expireIn)
        {
            Logger.TraceFormat("IncrementCounter key={0}, expireIn={1}", key, expireIn);

            AcquireCounterLock();

            QueueCommand(x =>
                x.Execute(
                    @"INSERT INTO ""Counter"" (""Id"", ""Key"", ""Value"", ""ExpireAt"") VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE, :EXPIRE_AT)",
                    new { KEY = key, VALUE = +1, EXPIRE_AT = DateTime.UtcNow.Add(expireIn) }));
        }

        public override void DecrementCounter(string key)
        {
            Logger.TraceFormat("DecrementCounter key={0}", key);

            AcquireCounterLock();

            QueueCommand(x =>
                x.Execute(
                    @"INSERT INTO ""Counter"" (""Id"", ""Key"",""Value"") VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE)",
                    new { KEY = key, VALUE = -1 }));
        }

        public override void DecrementCounter(string key, TimeSpan expireIn)
        {
            Logger.TraceFormat("DecrementCounter key={0} expireIn={1}", key, expireIn);

            AcquireCounterLock();
            QueueCommand(x =>
                x.Execute(
                    @"INSERT INTO ""Counter"" (""Id"", ""Key"",""Value"", ""ExpireAt"") VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE, :EXPIRE_AT)",
                    new { KEY = key, VALUE = -1, EXPIRE_AT = DateTime.UtcNow.Add(expireIn) }));
        }

        public override void AddToSet(string key, string value)
        {
            AddToSet(key, value, 0.0);
        }

        public override void AddToSet(string key, string value, double score)
        {
            Logger.TraceFormat("AddToSet key={0} value={1}", key, value);

            AcquireSetLock();

            QueueCommand(x => x.Execute(
                @"
 MERGE INTO ""Set"" H
      USING (SELECT 1 FROM DUAL) SRC
         ON (H.""Key"" = :KEY AND H.""Value"" = :VALUE)
 WHEN MATCHED THEN
      UPDATE SET ""Score"" = :SCORE
 WHEN NOT MATCHED THEN
      INSERT (""Id"", ""Key"", ""Value"", ""Score"")
      VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE, :SCORE)
",
                new { KEY = key, VALUE = value, SCORE = score }));
        }

        public override void AddRangeToSet(string key, IList<string> items)
        {
            Logger.TraceFormat("AddRangeToSet key={0}", key);

            if (key == null) throw new ArgumentNullException(nameof(key));
            if (items == null) throw new ArgumentNullException(nameof(items));

            AcquireSetLock();
            QueueCommand(x =>
                x.Execute(
                    @"INSERT INTO ""Set"" (""Id"", ""Key"", ""Value"", ""Score"") VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE, 0.0)",
                    items.Select(value => new { KEY = key, VALUE = value }).ToList()));
        }


        public override void RemoveFromSet(string key, string value)
        {
            Logger.TraceFormat("RemoveFromSet key={0} value={1}", key, value);

            AcquireSetLock();
            QueueCommand(x => x.Execute(@"DELETE FROM ""Set"" WHERE ""Key"" = :KEY AND ""Value"" = :VALUE", new { KEY = key, VALUE = value }));
        }

        public override void ExpireSet(string key, TimeSpan expireIn)
        {
            Logger.TraceFormat("ExpireSet key={0} expirein={1}", key, expireIn);

            if (key == null) throw new ArgumentNullException(nameof(key));

            AcquireSetLock();
            QueueCommand(x =>
                x.Execute(
                    @"UPDATE ""Set"" SET ""ExpireAt"" = :EXPIRE_AT WHERE ""Key"" = :KEY",
                    new { KEY = key, EXPIRE_AT = DateTime.UtcNow.Add(expireIn) }));
        }

        public override void InsertToList(string key, string value)
        {
            Logger.TraceFormat("InsertToList key={0} value={1}", key, value);

            AcquireListLock();

            var dmDynamicParameters = new DynamicParameters();
            dmDynamicParameters.Add("KEY", key);
            dmDynamicParameters.Add("VALUE", value, DbType.String, ParameterDirection.Input);

            QueueCommand(x => x.Execute(@"INSERT INTO ""List"" (""Id"",""Key"",""Value"") VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE)", dmDynamicParameters));
        }
        
        public override void ExpireList(string key, TimeSpan expireIn)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            Logger.TraceFormat("ExpireList key={0} expirein={1}", key, expireIn);

            AcquireListLock();
            QueueCommand(x =>
                x.Execute(
                    @"UPDATE ""List"" SET ""ExpireAt"" = :EXPIRE_AT WHERE ""Key"" = :KEY",
                    new { KEY = key, EXPIRE_AT = DateTime.UtcNow.Add(expireIn) }));
        }

        public override void RemoveFromList(string key, string value)
        {
            Logger.TraceFormat("RemoveFromList key={0} value={1}", key, value);

            AcquireListLock();
            QueueCommand(x => x.Execute(
                @"DELETE FROM ""List"" WHERE ""Key"" = :KEY AND ""Value"" = :VALUE",
                new { KEY = key, VALUE = value }));
        }

        public override void TrimList(string key, int keepStartingFrom, int keepEndingAt)
        {
            Logger.TraceFormat("TrimList key={0} from={1} to={2}", key, keepStartingFrom, keepEndingAt);

            AcquireListLock();
            QueueCommand(x => x.Execute(
                @"
DELETE FROM ""List""
WHERE ""Id"" IN (
    SELECT ""Id""
    FROM (
        SELECT 
            ""Id"",
            ROW_NUMBER() OVER (ORDER BY ""Id"") AS ""rankvalue""
        FROM ""List""
        WHERE ""Key"" = :key
    ) ranked
    WHERE ""rankvalue"" NOT BETWEEN :start AND :end  
);",
                new { key, start = keepStartingFrom + 1, end = keepEndingAt + 1 }));
        }

        public override void PersistHash(string key)
        {
            Logger.TraceFormat("PersistHash key={0} ", key);

            if (key == null) throw new ArgumentNullException(nameof(key));

            AcquireHashLock();
            QueueCommand(x =>
                x.Execute(
                    @"UPDATE ""Hash"" SET ""ExpireAt"" = NULL WHERE ""Key"" = :KEY", new { KEY = key }));
        }

        public override void PersistSet(string key)
        {
            Logger.TraceFormat("PersistSet key={0} ", key);

            if (key == null) throw new ArgumentNullException(nameof(key));

            AcquireSetLock();
            QueueCommand(x => x.Execute(@"UPDATE ""Set"" SET ""ExpireAt"" = NULL WHERE ""Key"" = :KEY", new { KEY = key }));
        }

        public override void RemoveSet(string key)
        {
            Logger.TraceFormat("RemoveSet key={0} ", key);

            if (key == null) throw new ArgumentNullException(nameof(key));

            AcquireSetLock();
            QueueCommand(x => x.Execute(@"DELETE FROM ""Set"" WHERE ""Key"" = :KEY", new { KEY = key }));
        }

        public override void PersistList(string key)
        {
            Logger.TraceFormat("PersistList key={0} ", key);

            if (key == null) throw new ArgumentNullException(nameof(key));

            AcquireListLock();
            QueueCommand(x =>
                x.Execute(
                    @"UPDATE ""List"" SET ""ExpireAt"" = NULL WHERE ""Key"" = :KEY", new { KEY = key }));
        }

        public override void SetRangeInHash(string key, IEnumerable<KeyValuePair<string, string>> keyValuePairs)
        {
            Logger.TraceFormat("SetRangeInHash key={0} ", key);

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (keyValuePairs == null)
            {
                throw new ArgumentNullException(nameof(keyValuePairs));
            }

            AcquireHashLock();
            QueueCommand(x =>
                x.Execute(
                    @"
 MERGE INTO ""Hash"" H
      USING (SELECT 1 FROM DUAL) SRC
         ON (H.""Key"" = :KEY AND H.""Field"" = :FIELD)
 WHEN MATCHED THEN
      UPDATE SET ""Value"" = :VALUE
 WHEN NOT MATCHED THEN
      INSERT (""Id"", ""Key"", ""Value"", ""Field"")
      VALUES (SEQUENCED.NEXTVAL, :KEY, :VALUE, :FIELD)
",
                    keyValuePairs.Select(y => new { KEY = key, FIELD = y.Key, VALUE = y.Value })));
        }

        public override void ExpireHash(string key, TimeSpan expireIn)
        {
            Logger.TraceFormat("ExpireHash key={0} ", key);

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            AcquireHashLock();
            QueueCommand(x =>
                x.Execute(
                    @"UPDATE ""Hash"" SET ""ExpireAt"" = :EXPIRE_AT WHERE ""Key"" = :KEY",
                    new { KEY = key, EXPIRE_AT = DateTime.UtcNow.Add(expireIn) }));
        }

        public override void RemoveHash(string key)
        {
            Logger.TraceFormat("RemoveHash key={0} ", key);

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            AcquireHashLock();
            QueueCommand(x => x.Execute(@"DELETE FROM ""Hash"" WHERE ""Key"" = :KEY", new { KEY = key }));
        }

        public override void Commit()
        {
            _storage.UseTransaction(connection =>
            {
                foreach (var command in _commandQueue)
                {
                    command(connection);
                }
            });
        }

        internal void QueueCommand(Action<IDbConnection> action)
        {
            _commandQueue.Enqueue(action);
        }

        private void AcquireJobLock()
        {
            AcquireLock("Job");
        }

        private void AcquireSetLock()
        {
            AcquireLock("Set");
        }

        private void AcquireListLock()
        {
            AcquireLock("List");
        }

        private void AcquireHashLock()
        {
            AcquireLock("Hash");
        }

        private void AcquireStateLock()
        {
            AcquireLock("State");
        }

        private void AcquireCounterLock()
        {
            AcquireLock("Counter");
        }
        private void AcquireLock(string resource)
        {
        }
    }
}
