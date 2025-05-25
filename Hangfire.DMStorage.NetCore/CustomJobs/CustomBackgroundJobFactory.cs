using Hangfire.Annotations;
using Hangfire.Client;
using Hangfire.States;

namespace Hangfire.DMStorage.NetCore.CustomJobs
{
    internal class CustomBackgroundJobFactory : IBackgroundJobFactory
    {
        private readonly IBackgroundJobFactory _inner;

        public CustomBackgroundJobFactory([NotNull] IBackgroundJobFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IStateMachine StateMachine => _inner.StateMachine;

        public BackgroundJob Create(CreateContext context)
        {
            Console.WriteLine($"Create: {context.Job.Type.FullName}.{context.Job.Method.Name} in {context.InitialState?.Name} state");
            return _inner.Create(context);
        }
    }
}
