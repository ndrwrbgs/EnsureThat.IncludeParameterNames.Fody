using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using AssemblyToProcess;
    using EnsureThat.IncludeParameterNames.Fody;
    using Fody;

    [TestClass]
    public class UnitTest1
    {
        /// <summary>
        /// The `nameof` of the argument in <see cref="SampleCodeToWeave.AssertMoreThanZero"/>
        /// </summary>
        const string ArgumentNameInTargetLibrary = "input";

        [TestMethod]
        public void Without_Weaving()
        {
            var exception = Assert.ThrowsException<ArgumentOutOfRangeException>(() => SampleCodeToWeave.AssertMoreThanZero(-1));
            Assert.IsFalse(exception.Message.Contains(ArgumentNameInTargetLibrary));
        }

        /// <summary>
        /// Inspiration: <see href="https://github.com/Fody/Home/blob/master/pages/addin-development.md#tests-project"/>
        /// </summary>
        [TestMethod]
        public void WithWeaving()
        {
            // # Arrange - Perform the weaving
            var weavingTask = new ModuleWeaver();
            var testResult = weavingTask.ExecuteTestRun($"{nameof(AssemblyToProcess)}.dll");
            testResult.Messages.ToList().ForEach(msg => Trace.WriteLine(msg.Text));
            testResult.Warnings.ToList().ForEach(msg => Trace.WriteLine(msg.Text));
            testResult.Errors.ToList().ForEach(msg => Trace.WriteLine(msg.Text));

            // # Act - Perform the operation
            var type = testResult.Assembly.GetType(typeof(SampleCodeToWeave).FullName);
            var method = type.GetMethod(nameof(SampleCodeToWeave.AssertMoreThanZero));
            // Note on how we are calling (don't use `.Invoke()`): https://stackoverflow.com/a/11140064/13888853
            var methodAsAction = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), method);
            var callMethod = new Action(() => methodAsAction(-1));

            // # Assert - Perform the assertion
            var exception = Assert.ThrowsException<ArgumentOutOfRangeException>(callMethod);
            Assert.IsTrue(exception.Message.Contains(ArgumentNameInTargetLibrary));
        }
    }
}
