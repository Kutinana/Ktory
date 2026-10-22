namespace Ktory.Core.Runtime
{
    public enum ExecutionStatus
    {
        Ready,
        SuspendedAtBeat,
        AwaitingChoice,
        Completed,
        Error,
        WaitingForClock = 7,
        WaitingForTrack = 8
    }

    public enum StepType
    {
        Text,
        Directive
    }
}
