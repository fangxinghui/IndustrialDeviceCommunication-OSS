namespace DeviceCommunication.Models
{
    /// <summary>
    /// Result of a communication call. This type is owned by this library and does not
    /// inherit from HslCommunication.OperateResult, so the core assembly builds without
    /// HslCommunication.
    /// </summary>
    public class OperationResult
    {
        public bool IsSuccess { get; set; }

        public string Message { get; set; } = string.Empty;

        public int ErrorCode { get; set; }

        public OperationResult()
        {
        }

        public OperationResult(string message)
        {
            Message = message ?? string.Empty;
        }

        public OperationResult(int errorCode, string message)
        {
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
        }

        public static OperationResult CreateSuccessResult()
        {
            return new OperationResult { IsSuccess = true };
        }

        public static OperationResult CreateFailedResult(string message)
        {
            return new OperationResult(message);
        }

        public override string ToString()
        {
            return IsSuccess ? "Success" : $"Failed ({ErrorCode}): {Message}";
        }
    }
}
