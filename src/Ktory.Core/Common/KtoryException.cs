using System;

namespace Ktory.Core.Common
{
    public class KtoryException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public KtoryException(string message) : base(message)
        {
        }

        public KtoryException(string message, int line, int column) 
            : base($"{message} (Line: {line}, Column: {column})")
        {
            Line = line;
            Column = column;
        }

        public KtoryException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class KtoryControlFlowException : KtoryException
    {
        public KtoryControlFlowException(string message) : base(message)
        {
        }
    }
}
