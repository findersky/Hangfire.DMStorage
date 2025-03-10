using System;

namespace Hangfire.DMStorage
{
    public class DMDistributedLockException : Exception
    {
        public DMDistributedLockException(string message) : base(message)
        {
        }
    }
}
