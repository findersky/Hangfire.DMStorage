using System;

namespace Hangfire.DMStorage.JobQueue
{
    internal class DMJobQueueProvider : IPersistentJobQueueProvider
    {
        private readonly IPersistentJobQueue _jobQueue;
        private readonly IPersistentJobQueueMonitoringApi _monitoringApi;

        public DMJobQueueProvider(DMStorage storage, DMStorageOptions options)
        {
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _jobQueue = new DMJobQueue(storage, options);
            _monitoringApi = new DMJobQueueMonitoringApi(storage);
        }

        public IPersistentJobQueue GetJobQueue()
        {
            return _jobQueue;
        }

        public IPersistentJobQueueMonitoringApi GetJobQueueMonitoringApi()
        {
            return _monitoringApi;
        }
    }
}