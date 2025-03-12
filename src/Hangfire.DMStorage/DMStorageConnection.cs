using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;

using Dapper;

using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.DMStorage.Entities;
using Hangfire.Server;
using Hangfire.Storage;
using Hangfire.DMStorage.CommonExtension;

namespace Hangfire.DMStorage
{
    public class DMStorageConnection : JobStorageConnection
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(DMStorageConnection));

        private readonly DMStorage _storage;
        public DMStorageConnection(DMStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public override IWriteOnlyTransaction CreateWriteTransaction()
        {
            return new DMWriteOnlyTransaction(_storage);
        }

        public override IDisposable AcquireDistributedLock(string resource, TimeSpan timeout)
        {
            return new DMDistributedLock(_storage, resource, timeout).Acquire();
        }

        public override string CreateExpiredJob(Job job, IDictionary<string, string> parameters, DateTime createdAt, TimeSpan expireIn)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var invocationData = InvocationData.SerializeJob(job);
            invocationData.Arguments = null;
            var arguments = InvocationData.SerializeJob(job);

            Logger.TraceFormat("CreateExpiredJob={0}", SerializationHelper.Serialize(invocationData, SerializationOption.User));

            return _storage.UseConnection(connection =>
            {
                var jobId = connection.GetNextJobId();

                var dmDynamicParameters = new DynamicParameters();
                dmDynamicParameters.AddDynamicParams(new
                {
                    ID = jobId,
                    CREATED_AT = createdAt,
                    EXPIRE_AT = createdAt.Add(expireIn)
                });
                dmDynamicParameters.Add("INVOCATION_DATA", SerializationHelper.Serialize(invocationData, SerializationOption.User), DbType.String, ParameterDirection.Input);
                dmDynamicParameters.Add("ARGUMENTS", arguments.Arguments, DbType.String, ParameterDirection.Input);

                connection.Execute(
                    @" 
 INSERT INTO ""Job"" (""Id"", ""InvocationData"", ""Arguments"", ""CreatedAt"", ExpireAt"") 
     VALUES (:ID, :INVOCATION_DATA, :ARGUMENTS, :CREATED_AT, :EXPIRE_AT)
",
                    dmDynamicParameters);

                if (parameters.Count > 0)
                {
                    var parameterArray = new object[parameters.Count];
                    var parameterIndex = 0;
                    foreach (var parameter in parameters)
                    {
                        var dynamicParameters = new DynamicParameters();
                        dynamicParameters.AddDynamicParams(new
                        {
                            JOB_ID = jobId,
                            NAME = parameter.Key
                        });
                        dynamicParameters.Add("VALUE", parameter.Value, DbType.String, ParameterDirection.Input);

                        parameterArray[parameterIndex++] = dynamicParameters;
                    }

                    connection.Execute(@"INSERT INTO ""JobParameter"" (""Id"", ""Name"", ""Value"",""JobId"") VALUES (SEQUENCED.NEXTVAL, :NAME, :VALUE, :JOB_ID)", parameterArray);
                }

                return jobId.ToString();
            });
        }

        public override IFetchedJob FetchNextJob(string[] queues, CancellationToken cancellationToken)
        {
            if (queues == null || queues.Length == 0)
            {
                throw new ArgumentNullException(nameof(queues));
            }

            var providers = queues
                .Select(queue => _storage.QueueProviders.GetProvider(queue))
                .Distinct()
                .ToArray();

            if (providers.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Multiple provider instances registered for queues: {string.Join(", ", queues)}. You should choose only one type of persistent queues per server instance.");
            }

            var persistentQueue = providers[0].GetJobQueue();
            return persistentQueue.Dequeue(queues, cancellationToken);
        }

        public override void SetJobParameter(string id, string name, string value)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            _storage.UseConnection(connection =>
            {
                var dmDynamicParameters = new DynamicParameters();
                dmDynamicParameters.AddDynamicParams(new { JOB_ID = id, NAME = name });
                dmDynamicParameters.Add("VALUE", value, DbType.String, ParameterDirection.Input);
                connection.Execute(
                    @" 
 
MERGE INTO ""JobParameter"" JP
     USING (SELECT 1 FROM DUAL) SRC
        ON (JP.""Name"" = :NAME AND JP.""JobId"" = :JOB_ID)
WHEN MATCHED THEN
     UPDATE SET ""Value"" = :VALUE
WHEN NOT MATCHED THEN
     INSERT (""Id"", ""JobId"", ""Name"", ""Value"")
     VALUES (SEQUENCED.NEXTVAL, :JOB_ID, :NAME, :VALUE)
",
                    dmDynamicParameters);
            });
        }

        public override string GetJobParameter(string id, string name)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return _storage.UseConnection(connection =>
                connection.QuerySingleOrDefault<string>(
                    @"SELECT ""Value""
                        FROM ""JobParameter""  WHERE ""JobId"" = :ID AND ""Name"" = :NAME",
                    new { ID = id, NAME = name }));
        }

        public override JobData GetJobData(string jobId)
        {
            if (jobId == null)
            {
                throw new ArgumentNullException(nameof(jobId));
            }

            return _storage.UseConnection(connection =>
            {
                var jobData = connection.QuerySingleOrDefault<SqlJob>(
                            @"SELECT ""InvocationData"" ,""StateName"", ""Arguments"", ""CreatedAt""
                              FROM ""Job"" WHERE ""Id""  = :ID",
                            new { ID = jobId });

                if (jobData == null)
                {
                    return null;
                }

                var invocationData = SerializationHelper.Deserialize<InvocationData>(jobData.InvocationData, SerializationOption.User);
                invocationData.Arguments = jobData.Arguments;

                Job job = null;
                JobLoadException loadException = null;

                try
                {
                    job = invocationData.DeserializeJob();
                }
                catch (JobLoadException ex)
                {
                    loadException = ex;
                }

                return new JobData
                {
                    Job = job,
                    State = jobData.StateName,
                    CreatedAt = jobData.CreatedAt,
                    LoadException = loadException
                };
            });
        }

        public override StateData GetStateData(string jobId)
        {
            if (jobId == null)
            {
                throw new ArgumentNullException(nameof(jobId));
            }

            return _storage.UseConnection(connection =>
            {
                var sqlState = connection.QuerySingleOrDefault<SqlState>(
                        @" SELECT S.""Name"", S.""Reason"",S.""Data""  FROM ""State"" S INNER JOIN ""Job"" J
                            ON J.""StateId"" = S.""Id""  WHERE J.""Id"" = :JOB_ID",
                        new { JOB_ID = jobId });

                if (sqlState == null)
                {
                    return null;
                }

                var data = new Dictionary<string, string>(
                    SerializationHelper.Deserialize<Dictionary<string, string>>(sqlState.Data, SerializationOption.User),
                    StringComparer.OrdinalIgnoreCase);

                return new StateData
                {
                    Name = sqlState.Name,
                    Reason = sqlState.Reason,
                    Data = data
                };
            });
        }

        public override void AnnounceServer(string serverId, ServerContext context)
        {
            if (serverId == null)
            {
                throw new ArgumentNullException(nameof(serverId));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _storage.UseConnection(connection =>
            {
                connection.Execute(
                    @"
 MERGE INTO ""Server"" S
      USING (SELECT 1 FROM DUAL) SRC
         ON (S.""Id"" = :ID)
 WHEN MATCHED THEN
      UPDATE SET ""LastHeartBeat"" =:LAST_HEART_BEAT
 WHEN NOT MATCHED THEN
      INSERT (""Id"" , ""Data"", ""LastHeartBeat"")
      VALUES (:ID, :DATA, :LAST_HEART_BEAT)
",
                    new
                    {
                        ID = serverId,
                        DATA = SerializationHelper.Serialize(new ServerData
                        {
                            WorkerCount = context.WorkerCount,
                            Queues = context.Queues,
                            StartedAt = DateTime.UtcNow,
                        }, SerializationOption.User),
                        LAST_HEART_BEAT = DateTime.UtcNow
                    });
            });
        }

        public override void RemoveServer(string serverId)
        {
            if (serverId == null)
            {
                throw new ArgumentNullException(nameof(serverId));
            }

            _storage.UseConnection(connection =>
            {
                connection.Execute(
                    @"DELETE FROM ""Server"" where ""Id"" = :ID",
                    new { ID = serverId });
            });
        }

        public override void Heartbeat(string serverId)
        {
            if (serverId == null)
            {
                throw new ArgumentNullException(nameof(serverId));
            }

            _storage.UseConnection(connection =>
            {
                connection.Execute(
                    @" UPDATE ""Server"" SET ""LastHeartBeat"" = :NOW WHERE ""Id"" = :ID",
                    new { NOW = DateTime.UtcNow, ID = serverId });
            });
        }

        public override int RemoveTimedOutServers(TimeSpan timeOut)
        {
            if (timeOut.Duration() != timeOut)
            {
                throw new ArgumentException("The `timeOut` value must be positive.", nameof(timeOut));
            }

            return
                _storage.UseConnection(connection =>
                    connection.Execute(
                        @" DELETE FROM ""Server"" WHERE ""LastHeartBeat"" < :TIME_OUT_AT",
                        new { TIME_OUT_AT = DateTime.UtcNow.Add(timeOut.Negate()) }));
        }

        public override long GetSetCount(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return
                _storage.UseConnection(connection =>
                    connection.QueryFirst<int>(
                        @"SELECT COUNT(""Key"")  FROM ""Set"" WHERE ""Key"" = :KEY",
                        new { KEY = key }));
        }

        public override List<string> GetRangeFromSet(string key, int startingFrom, int endingAt)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return _storage.UseConnection(connection =>
                connection.Query<string>(@"
SELECT ""Value""
  FROM (SELECT ""Value"", RANK () OVER (ORDER BY ""Id"") AS RANK
          FROM ""Set""
         WHERE ""Key"" = :KEY)
 WHERE RANK BETWEEN :S AND :E
",
                        new { KEY = key, S = startingFrom + 1, E = endingAt + 1 }).ToList());
        }

        public override HashSet<string> GetAllItemsFromSet(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return
                _storage.UseConnection(connection =>
                {
                    var result = connection.Query<string>(
                        @"SELECT ""Value""  FROM ""Set"" WHERE ""Key"" = :KEY",
                        new { KEY = key });

                    return new HashSet<string>(result);
                });
        }

        public override string GetFirstByLowestScoreFromSet(string key, double fromScore, double toScore)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (toScore < fromScore)
            {
                throw new ArgumentException("The `toScore` value must be higher or equal to the `fromScore` value.");
            }

            return
                _storage.UseConnection(connection =>
                    connection.QuerySingleOrDefault<string>(
                        @"
SELECT *
  FROM (  SELECT ""Value""
            FROM ""Set""
           WHERE ""Key"" = :KEY AND ""Score"" BETWEEN :F AND :T
        ORDER BY ""Score"")
 WHERE ROWNUM = 1
",
                        new { KEY = key, F = fromScore, T = toScore }));
        }

        public override long GetCounter(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            const string query = @"
SELECT SUM(S.""Value"")
  FROM (SELECT SUM(""Value"") AS ""Value""
  FROM ""Counter""
 WHERE ""Key"" = :KEY
UNION ALL
SELECT ""Value""
 FROM ""AggregatedCounter""
WHERE ""Key"" = :KEY) AS S";

            return
                _storage
                    .UseConnection(connection =>
                        connection.QuerySingle<long?>(query, new { KEY = key }) ?? 0);
        }

        public override long GetHashCount(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return
                _storage
                    .UseConnection(connection =>
                        connection.QuerySingle<long>(
                            @"SELECT COUNT(""Id"") FROM ""Hash"" WHERE ""Key"" = :KEY",
                            new { KEY = key }));
        }

        public override TimeSpan GetHashTtl(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return _storage.UseConnection(connection =>
            {
                var result =
                    connection.QuerySingle<DateTime?>(
                        @"SELECT MIN(""ExpireAt"") FROM ""Hash"" WHERE ""Key"" = :KEY",
                        new { KEY = key });

                if (!result.HasValue)
                {
                    return TimeSpan.FromSeconds(-1);
                }

                return result.Value - DateTime.UtcNow;
            });
        }

        public override long GetListCount(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return
                _storage
                    .UseConnection(connection =>
                        connection.QuerySingle<long>(
                            @"SELECT COUNT(""Id"") FROM ""List"" WHERE ""Key"" = :KEY",
                            new { KEY = key }));
        }

        public override TimeSpan GetListTtl(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return _storage.UseConnection(connection =>
            {
                var result = connection.QuerySingle<DateTime?>(
                        @"SELECT MIN(""ExpireAt"") FROM ""List"" WHERE ""Key"" = :KEY",
                        new { KEY = key });

                if (!result.HasValue)
                {
                    return TimeSpan.FromSeconds(-1);
                }

                return result.Value - DateTime.UtcNow;
            });
        }

        public override string GetValueFromHash(string key, string name)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (name == null) throw new ArgumentNullException(nameof(name));

            return
                _storage
                    .UseConnection(connection =>
                        connection.QuerySingleOrDefault<string>(
                            @"SELECT ""Value"" FROM ""Hash"" WHERE ""Key"" = :KEY and ""Field"" = :FIELD",
                            new { KEY = key, FIELD = name }));
        }

        public override List<string> GetRangeFromList(string key, int startingFrom, int endingAt)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            const string query = @"
SELECT ""Value""
  FROM (SELECT ""Value"", RANK () OVER (ORDER BY ""Id"" DESC) AS RANK
          FROM ""List""
         WHERE ""Key"" = :KEY)
 WHERE RANK BETWEEN :S AND :E
";
            return
                _storage
                    .UseConnection(connection =>
                        connection.Query<string>(query,
                            new { KEY = key, S = startingFrom + 1, E = endingAt + 1 })
                            .ToList());
        }

        public override List<string> GetAllItemsFromList(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            const string query = @"
 SELECT ""Value""
   FROM ""List""
  WHERE ""Key"" = :KEY
 ORDER BY ""Id"" DESC";

            return _storage.UseConnection(connection => connection.Query<string>(query, new { KEY = key }).ToList());
        }

        public override TimeSpan GetSetTtl(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            return _storage.UseConnection(connection =>
            {
                var result = connection.QuerySingle<DateTime?>(@"SELECT MIN(""ExpireAt"") FROM ""Set"" WHERE ""Key"" = :KEY", new { KEY = key });

                if (!result.HasValue)
                {
                    return TimeSpan.FromSeconds(-1);
                }

                return result.Value - DateTime.UtcNow;
            });
        }

        public override void SetRangeInHash(string key, IEnumerable<KeyValuePair<string, string>> keyValuePairs)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (keyValuePairs == null)
            {
                throw new ArgumentNullException(nameof(keyValuePairs));
            }

            _storage.UseTransaction(connection =>
            {
                foreach (var keyValuePair in keyValuePairs)
                {
                    var dmDynamicParameters = new DynamicParameters();
                    dmDynamicParameters.AddDynamicParams(new { KEY = key, FIELD = keyValuePair.Key });
                    dmDynamicParameters.Add("VALUE", keyValuePair.Value, DbType.String, ParameterDirection.Input);

                    connection.Execute(
                        @"
 MERGE INTO ""Hash"" H
     USING (SELECT 1 FROM DUAL) SRC
        ON (H.""Key"" = :KEY AND H.""Field"" = :FIELD)
WHEN MATCHED THEN
    UPDATE SET ""Value"" = :VALUE
WHEN NOT MATCHED THEN
    INSERT (""Id"", ""Key"", ""Field"", ""Value"")
    VALUES (SEQUENCED.NEXTVAL, :KEY, :FIELD, :VALUE)
",
                        dmDynamicParameters);
                }
            });
        }

        public override Dictionary<string, string> GetAllEntriesFromHash(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return _storage.UseConnection(connection =>
            {
                var result = connection.Query<SqlHash>(
                    @"SELECT ""Field"", ""Value"" FROM ""Hash"" WHERE ""Key"" = :KEY",
                    new { KEY = key })
                    .ToDictionary(x => x.Field, x => x.Value);

                return result.Count != 0 ? result : null;
            });
        }
    }
}