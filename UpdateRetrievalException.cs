using System;
using System.IO;

namespace ChatGPTInstaller
{
    internal sealed class UpdateRetrievalException : IOException
    {
        public readonly UpdateRetrievalStage Stage;
        public readonly bool InvalidatesPackage;

        public UpdateRetrievalException(UpdateRetrievalStage stage, string message, Exception inner = null, bool invalidatesPackage = false)
            : base(message, inner)
        {
            Stage = stage;
            InvalidatesPackage = invalidatesPackage;
        }
    }
}
