// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis
{
    internal struct StackGuard
    {
        public const int MaxUncheckedRecursionDepth = 20;

        // this is a rather arbitrary big number, which should not be achievable except in cases of infinite recursion.
        // it is basically a criteria when we prefer to failfast instead of eventually OOM, which could take long time on 64bit
        private const int MaxExecutionStackCount = 1024;

        private ushort _recursionDepth;
        private ushort _executionStackCount;


        /// <summary>
        ///     Ensures that the remaining stack space is large enough to execute
        ///     the average function.
        /// </summary>
        /// <param name="recursionDepth">how many times the calling function has recursed</param>
        /// <exception cref="InsufficientExecutionStackException">
        ///     The available stack space is insufficient to execute
        ///     the average function.
        /// </exception>
        public static void EnsureSufficientExecutionStack(int recursionDepth)
        {
            if (recursionDepth > MaxUncheckedRecursionDepth)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
        }

        public bool TryEnterOnCurrentStack()
        {
            if (_recursionDepth < MaxUncheckedRecursionDepth)
            {
                _recursionDepth++;
                return true;
            }

            return TryEnterOnCurrentStackChecked();
        }

        private bool TryEnterOnCurrentStackChecked()
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException) when (_executionStackCount < MaxExecutionStackCount)
            {
                return false;
            }

            _recursionDepth++;
            return true;
        }

        public void Leave()
        {
            _recursionDepth--;
        }

        public void RunOnEmptyStack<T1, T2>(Action<T1, T2> action, T1 arg1, T2 arg2)
        {
            RunOnEmptyStackCore(s =>
            {
                var t = ((Action<T1, T2>, T1, T2))s;
                t.Item1(t.Item2, t.Item3);
                return default(object);
            }, (action, arg1, arg2));
        }

        public void RunOnEmptyStack<T1, T2, T3>(Action<T1, T2, T3> action, T1 arg1, T2 arg2, T3 arg3)
        {
            RunOnEmptyStackCore(s =>
            {
                var t = ((Action<T1, T2, T3>, T1, T2, T3))s;
                t.Item1(t.Item2, t.Item3, t.Item4);
                return default(object);
            }, (action, arg1, arg2, arg3));
        }

        public R RunOnEmptyStack<T1, R>(Func<T1, R> action, T1 arg1)
        {
            return RunOnEmptyStackCore(s =>
            {
                var t = ((Func<T1, R>, T1))s;
                return t.Item1(t.Item2);
            }, (action, arg1));
        }

        public R RunOnEmptyStack<T1, T2, R>(Func<T1, T2, R> action, T1 arg1, T2 arg2)
        {
            return RunOnEmptyStackCore(s =>
            {
                var t = ((Func<T1, T2, R>, T1, T2))s;
                return t.Item1(t.Item2, t.Item3);
            }, (action, arg1, arg2));
        }

        public R RunOnEmptyStack<T1, T2, T3, R>(Func<T1, T2, T3, R> action, T1 arg1, T2 arg2, T3 arg3)
        {
            return RunOnEmptyStackCore(s =>
            {
                var t = ((Func<T1, T2, T3, R>, T1, T2, T3))s;
                return t.Item1(t.Item2, t.Item3, t.Item4);
            }, (action, arg1, arg2, arg3));
        }

        private R RunOnEmptyStackCore<R>(Func<object, R> action, object state)
        {
            var origRecursionDepth = _recursionDepth;
            _recursionDepth = 0;
            _executionStackCount++;

            try
            {
                // Using default scheduler rather than picking up the current scheduler.
                Task<R> task = Task.Factory.StartNew(action, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

                TaskAwaiter<R> awaiter = task.GetAwaiter();

                // Avoid AsyncWaitHandle lazy allocation of ManualResetEvent in the rare case we finish quickly.
                if (!awaiter.IsCompleted)
                {
                    // Task.Wait has the potential of inlining the task's execution on the current thread; avoid this.
                    ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
                }

                // Using awaiter here to unwrap AggregateException.
                return awaiter.GetResult();
            }
            finally
            {
                _recursionDepth = origRecursionDepth;
                _executionStackCount--;
            }
        }

    }
}
