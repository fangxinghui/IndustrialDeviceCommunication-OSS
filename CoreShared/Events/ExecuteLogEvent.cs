namespace CoreShared.Events
{
    public class ExecuteLogEvent
    {
        public string? ClassName { get; set; }
        public string? MethodName { get; set; }
        public string? LogMessage { get; set; }
    }
}