using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Jobs
{
    public class ScheduledTask : ModelBase
    {
        public string TypeName { get; set; }
        public int Interval { get; set; }
        public int? IntervalSeconds { get; set; }
        public DateTime LastExecution { get; set; }
        public CommandPriority Priority { get; set; }
        public DateTime LastStartTime { get; set; }

        public TimeSpan GetInterval()
        {
            return IntervalSeconds.HasValue ? TimeSpan.FromSeconds(IntervalSeconds.Value) : TimeSpan.FromMinutes(Interval);
        }

        public ScheduledTask()
        {
            Priority = CommandPriority.Low;
        }
    }
}
