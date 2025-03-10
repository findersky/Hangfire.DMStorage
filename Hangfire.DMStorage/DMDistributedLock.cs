using System;
using System.Data;
using System.Threading;

using Dapper;

using Hangfire.Logging;

namespace Hangfire.DMStorage
{
    public class DMDistributedLock : IDisposable, IComparable
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(DMDistributedLock));
        private readonly TimeSpan _timeout;
        private readonly DMStorage _storage;
        private readonly DateTime _start;
        private readonly CancellationToken _cancellationToken;

        private const int DelayBetweenPasses = 100;

        public DMDistributedLock(DMStorage storage, string resource, TimeSpan timeout)
            : this(storage.CreateAndOpenConnection(), resource, timeout)
        {
            _storage = storage;
        }

        private readonly IDbConnection _connection;

        public DMDistributedLock(IDbConnection connection, string resource, TimeSpan timeout)
            : this(connection, resource, timeout, new CancellationToken())
        {
        }

        public DMDistributedLock(IDbConnection connection, string resource, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Logger.TraceFormat("DMDistributedLock resource={0}, timeout={1}", resource, timeout);

            Resource = resource;
            _timeout = timeout;
            _connection = connection;
            _cancellationToken = cancellationToken;
            _start = DateTime.UtcNow;
        }

        public string Resource { get; }

        private int AcquireLock(string resource, TimeSpan timeout)
        {
            return
                _connection
                    .Execute(
                        @" 
IINSERT INTO ""DistributedLock"" (""Resource"", ""CreatedAt"")
                (SELECT :RES, :NOW
                FROM DUAL
                WHERE NOT EXISTS
            (SELECT ""Resource"", ""CreatedAt""
                FROM ""DistributedLock""
                WHERE ""Resource"" = :RES AND ""CreatedAt"" > :EXPIRED))
", 
                        new
                        {
                            RES = resource,
                            NOW = DateTime.UtcNow,
                            EXPIRED = DateTime.UtcNow.Add(timeout.Negate())
                        });
        }

        public void Dispose()
        {
            Release();

            _storage?.ReleaseConnection(_connection);
        }

        internal DMDistributedLock Acquire()
        {
            Logger.TraceFormat("Acquire resource={0}, timeout={1}", Resource, _timeout);

            int insertedObjectCount;
            do
            {
                _cancellationToken.ThrowIfCancellationRequested();

                insertedObjectCount = AcquireLock(Resource, _timeout);

                if (ContinueCondition(insertedObjectCount))
                {
                    _cancellationToken.WaitHandle.WaitOne(DelayBetweenPasses);
                    _cancellationToken.ThrowIfCancellationRequested();
                }
            } while (ContinueCondition(insertedObjectCount));

            if (insertedObjectCount == 0)
            {
                throw new DMDistributedLockException("cannot acquire lock");
            }
            return this;
        }

        private bool ContinueCondition(int insertedObjectCount)
        {
            return insertedObjectCount == 0 && _start.Add(_timeout) > DateTime.UtcNow;
        }

        internal void Release()
        {
            Logger.TraceFormat("Release resource={0}", Resource);

            _connection
                .Execute(
                    @"
DELETE FROM ""DistributedLock"" 
 WHERE ""Resource"" = :RES
",
                    new
                    {
                        RES = Resource
                    });
        }

        public int CompareTo(object obj)
        {
            if (obj == null)
            {
                return 1;
            }

            if (obj is DMDistributedLock DMDistributedLock)
            {
                return string.Compare(Resource, DMDistributedLock.Resource, StringComparison.OrdinalIgnoreCase);
            }
            
            throw new ArgumentException("Object is not a DMDistributedLock");
        }
    }
}