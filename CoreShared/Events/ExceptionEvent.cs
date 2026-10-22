using System;

namespace CoreShared.Events
{
    public class ExceptionEvent: EventArgs
    {
        public string? ClassName { get; set; }
        public string? MethodName { get; set; }
        public string? ErrorMessage { get; set; }
    }
}