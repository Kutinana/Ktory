namespace Ktory.Core.Runtime
{
    public enum ExecutionStatus
    {
        Ready,
        SuspendedAtBeat,
        AwaitingChoice,
        Completed,
        Error
    }

    public enum StepType
    {
        Text,
        Directive
    }
}
