namespace AssemblyToProcess
{
    using System;
    using EnsureThat;

    public static class SampleCodeToWeave
    {
        public static void AssertMoreThanZero(int input)
        {
            Ensure.That(input).IsGt(0);
        }

        internal static class OnErrorUse<TException>
        {
            public static OptsFn Handler = default;
        }
    }
}
