using System;

namespace OwaspSanitizerNet.Html
{
    public class AssertionException : Exception
    {
        public AssertionException()
            : base()
        {
        }
        public AssertionException(string message)
            : base(message)
        {
        }
    }
}
