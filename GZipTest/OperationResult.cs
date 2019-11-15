using System;

namespace GZipTest
{
    public class OperationResult
    {
        public OperationResultEnum Result { get; set; }

        public Exception ThrownException { get; set; }
    }

    public enum OperationResultEnum
    {
        Failure,
        Success
    }
}
