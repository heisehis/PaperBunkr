using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Paperbunkr.App.Tests.PinnedThreadTestFramework", "Paperbunkr.App.Tests")]

namespace Paperbunkr.App.Tests;

/// <summary>
/// The one thread that owns <c>Dispatcher.UIThread</c> for the whole test run. Avalonia binds its
/// dispatcher to whichever thread first touches it, and RunJobs()/UI-affine objects only work from
/// that thread. Without this, xunit gave no guarantee: the collection-fixture constructor that
/// bootstrapped Avalonia ran on one pool thread (observed tid 4) while test bodies ran on another
/// (tid 22), so <c>CheckAccess()</c> was false and every "delete, pump the dispatcher, assert" test
/// silently saw an un-pumped queue, depending only on which classes had run earlier.
///
/// Deliberately a plain thread with NO SynchronizationContext: an earlier attempt that installed one
/// deadlocked every test that blocks on async work with GetAwaiter().GetResult() (six test files do),
/// because the continuation was queued onto the very thread doing the blocking.
/// </summary>
public static class PinnedThread
{
    private static readonly BlockingCollection<Action> Queue = new();

    private static readonly Thread Worker = StartWorker();

    private static Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var work in Queue.GetConsumingEnumerable())
            {
                // Avalonia's Setup() installs an AvaloniaSynchronizationContext on the thread it runs on.
                // Left in place, xunit's AsyncTestSyncContext wraps it and forwards every test-time
                // SynchronizationContext.Post into the dispatcher queue that nothing pumps, so the test
                // never completes (observed: RunAsync stuck in WaitingForActivation). Tests that ran on
                // other threads never had an ambient context; keep that behaviour.
                SynchronizationContext.SetSynchronizationContext(null);
                work();
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }, maxStackSize: 16 * 1024 * 1024)
        {
            Name = "Paperbunkr.Tests.Pinned",
            IsBackground = true,
        };
        thread.Start();
        return thread;
    }

    public static bool IsCurrent => Thread.CurrentThread == Worker;

    /// <summary>
    /// Runs <paramref name="work"/> on the pinned thread and blocks until it returns (the calling
    /// thread waits on a plain event, so nothing here can re-enter the caller).
    /// </summary>
    public static T Run<T>(Func<T> work)
    {
        if (IsCurrent)
        {
            return work();
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task.GetAwaiter().GetResult();
    }
}

/// <summary>
/// Wraps every test case so its synchronous execution (test-class construction, the test body up to
/// its first real await, Dispose) happens on <see cref="PinnedThread"/>. For a synchronous test that
/// is the whole test. An async test resumes wherever its awaits resume, exactly as before.
/// </summary>
public sealed class PinnedThreadTestFramework : XunitTestFramework
{
    public PinnedThreadTestFramework(IMessageSink messageSink) : base(messageSink)
    {
    }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new PinnedExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class PinnedExecutor : XunitTestFrameworkExecutor
    {
        public PinnedExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider,
            IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
        }

        protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases,
            IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            var pinned = testCases.Select(tc => (IXunitTestCase)new PinnedTestCase(tc)).ToList();
            using var runner = new XunitTestAssemblyRunner(
                TestAssembly, pinned, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class PinnedTestCase : LongLivedMarshalByRefObject, IXunitTestCase
    {
        private IXunitTestCase _inner = null!;

        /// <summary>Required by IXunitSerializable; never used, the wrapper is created after deserialization.</summary>
        public PinnedTestCase()
        {
        }

        public PinnedTestCase(IXunitTestCase inner) => _inner = inner;

        public string DisplayName => _inner.DisplayName;
        public string SkipReason => _inner.SkipReason;
        public ISourceInformation SourceInformation
        {
            get => _inner.SourceInformation;
            set => _inner.SourceInformation = value;
        }
        public ITestMethod TestMethod => _inner.TestMethod;
        public object[] TestMethodArguments => _inner.TestMethodArguments;
        public Dictionary<string, List<string>> Traits => _inner.Traits;
        public string UniqueID => _inner.UniqueID;
        public IMethodInfo Method => _inner.Method;
        public Exception InitializationException => _inner.InitializationException;
        public int Timeout => _inner.Timeout;

        public Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus,
            object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
            PinnedThread.Run(() => _inner.RunAsync(
                diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource));

        public void Serialize(IXunitSerializationInfo info) => _inner.Serialize(info);

        public void Deserialize(IXunitSerializationInfo info) => _inner.Deserialize(info);
    }
}
