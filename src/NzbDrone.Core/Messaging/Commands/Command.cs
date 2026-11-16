using System;
using System.Text.Json.Serialization;
using System.Threading;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Messaging.Commands
{
    [JsonConverter(typeof(PolymorphicWriteOnlyJsonConverter<Command>))]
    public abstract class Command
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly object _cancellationGate = new object();
        private bool _sendUpdatesToClient;
        private bool _finalizing;

        public virtual bool SupportsCancellation => false;

        [JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public CancellationToken CancellationToken => SupportsCancellation ? _cancellation.Token : System.Threading.CancellationToken.None;

        public bool CancellationRequested => _cancellation.IsCancellationRequested;

        public bool CanCancelRunning
        {
            get
            {
                lock (_cancellationGate)
                {
                    return SupportsCancellation && !_finalizing && !CancellationRequested;
                }
            }
        }

        internal bool RequestCancellation()
        {
            lock (_cancellationGate)
            {
                if (!SupportsCancellation || _finalizing)
                {
                    return false;
                }

                _cancellation.Cancel();
                return true;
            }
        }

        internal void BeginFinalization()
        {
            lock (_cancellationGate)
            {
                CancellationToken.ThrowIfCancellationRequested();
                _finalizing = true;
            }
        }

        public virtual bool SendUpdatesToClient
        {
            get
            {
                return _sendUpdatesToClient;
            }

            set
            {
                _sendUpdatesToClient = value;
            }
        }

        public virtual bool UpdateScheduledTask => true;
        public virtual string CompletionMessage => null;
        public virtual bool RequiresDiskAccess => false;
        public virtual bool IsExclusive => false;
        public virtual bool IsTypeExclusive => false;
        public virtual bool IsLongRunning => false;

        public string Name { get; private set; }
        public DateTime? LastExecutionTime { get; set; }
        public DateTime? LastStartTime { get; set; }
        public CommandTrigger Trigger { get; set; }
        public bool SuppressMessages { get; set; }

        public string ClientUserAgent { get; set; }

        public Command()
        {
            Name = GetType().Name.Replace("Command", "");
        }
    }
}
