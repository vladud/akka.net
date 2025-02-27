﻿//-----------------------------------------------------------------------
// <copyright file="Sources.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class QueueSource<TOut> : GraphStageWithMaterializedValue<SourceShape<TOut>, ISourceQueueWithComplete<TOut>>
    {
        #region internal classes

        /// <summary>
        /// TBD
        /// </summary>
        public interface IInput { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        internal sealed class Offer<T> : IInput
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
            /// <param name="completionSource">TBD</param>
            public Offer(T element, TaskCompletionSource<IQueueOfferResult> completionSource)
            {
                Element = element;
                CompletionSource = completionSource;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public T Element { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TaskCompletionSource<IQueueOfferResult> CompletionSource { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Completion : IInput
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static Completion Instance { get; } = new Completion();

            private Completion()
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Failure : IInput
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ex">TBD</param>
            public Failure(Exception ex)
            {
                Ex = ex;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Exception Ex { get; }
        }

        #endregion

        private sealed class Logic : GraphStageLogicWithCallbackWrapper<IInput>, IOutHandler
        {
            private readonly TaskCompletionSource<object> _completion;
            private readonly QueueSource<TOut> _stage;
            private IBuffer<TOut> _buffer;
            private Offer<TOut> _pendingOffer;
            private bool _terminating;

            public Logic(QueueSource<TOut> stage, TaskCompletionSource<object> completion) : base(stage.Shape)
            {
                _completion = completion;
                _stage = stage;

                SetHandler(stage.Out, this);
            }

            public void OnPull()
            {
                if (_stage._maxBuffer == 0)
                {
                    if (_pendingOffer != null)
                    {
                        Push(_stage.Out, _pendingOffer.Element);
                        _pendingOffer.CompletionSource.NonBlockingTrySetResult(QueueOfferResult.Enqueued.Instance);
                        _pendingOffer = null;
                        if (_terminating)
                        {
                            _completion.SetResult(new object());
                            CompleteStage();
                        }
                    }
                }
                else if (_buffer.NonEmpty)
                {
                    Push(_stage.Out, _buffer.Dequeue());
                    if (_pendingOffer != null)
                    {
                        EnqueueAndSuccess(_pendingOffer);
                        _pendingOffer = null;
                    }
                }

                if (_terminating && _buffer.IsEmpty)
                {
                    _completion.SetResult(new object());
                    CompleteStage();
                }
            }

            public void OnDownstreamFinish()
            {
                if (_pendingOffer != null)
                {
                    _pendingOffer.CompletionSource.NonBlockingTrySetResult(QueueOfferResult.QueueClosed.Instance);
                    _pendingOffer = null;
                }
                _completion.SetResult(new object());
                CompleteStage();
            }

            public override void PreStart()
            {
                if (_stage._maxBuffer > 0)
                    _buffer = Buffer.Create<TOut>(_stage._maxBuffer, Materializer);
                InitCallback(Callback());
            }

            public override void PostStop()
            {
                var exception = new StreamDetachedException();
                _completion.TrySetException(exception);
                StopCallback(input =>
                {
                    if (!(input is Offer<TOut> offer)) return;
                    var promise = offer.CompletionSource;
                    promise.NonBlockingTrySetException(exception);
                });
            }

            private void EnqueueAndSuccess(Offer<TOut> offer)
            {
                EnqueueAndSuccess(offer, QueueOfferResult.Enqueued.Instance);
            }

            private void EnqueueAndSuccess(Offer<TOut> offer, QueueOfferResult.Enqueued result)
            {
                _buffer.Enqueue(offer.Element);
                offer.CompletionSource.NonBlockingTrySetResult(result);
            }

            private void BufferElement(Offer<TOut> offer)
            {
                if (!_buffer.IsFull)
                    EnqueueAndSuccess(offer);
                else
                {
                    switch (_stage._overflowStrategy)
                    {
                        case OverflowStrategy.DropHead:
                            _buffer.DropHead();
                            EnqueueAndSuccess(offer, QueueOfferResult.Enqueued.DroppedHeadInstance);
                            break;
                        case OverflowStrategy.DropTail:
                            _buffer.DropTail();
                            EnqueueAndSuccess(offer, QueueOfferResult.Enqueued.DroppedTailInstance);
                            break;
                        case OverflowStrategy.DropBuffer:
                            _buffer.Clear();
                            EnqueueAndSuccess(offer, QueueOfferResult.Enqueued.DroppedBufferInstance);
                            break;
                        case OverflowStrategy.DropNew:
                            offer.CompletionSource.NonBlockingTrySetResult(QueueOfferResult.Dropped.Instance);
                            break;
                        case OverflowStrategy.Fail:
                            var bufferOverflowException =
                                new BufferOverflowException($"Buffer overflow (max capacity was: {_stage._maxBuffer})!");
                            offer.CompletionSource.NonBlockingTrySetResult(new QueueOfferResult.Failure(bufferOverflowException));
                            _completion.SetException(bufferOverflowException);
                            FailStage(bufferOverflowException);
                            break;
                        case OverflowStrategy.Backpressure:
                            if (_pendingOffer != null)
                                offer.CompletionSource.NonBlockingTrySetException(
                                    new IllegalStateException(
                                        "You have to wait for previous offer to be resolved to send another request."));
                            else
                                _pendingOffer = offer;
                            break;
                    }
                }
            }

            private Action<IInput> Callback()
            {
                return GetAsyncCallback<IInput>(
                    input =>
                    {
                        var offer = input as Offer<TOut>;
                        if (offer != null)
                        {
                            if (_stage._maxBuffer != 0)
                            {
                                BufferElement(offer);
                                if (IsAvailable(_stage.Out))
                                    Push(_stage.Out, _buffer.Dequeue());
                            }
                            else if (IsAvailable(_stage.Out))
                            {
                                Push(_stage.Out, offer.Element);
                                offer.CompletionSource.NonBlockingTrySetResult(QueueOfferResult.Enqueued.Instance);
                            }
                            else if (_pendingOffer == null)
                                _pendingOffer = offer;
                            else
                            {
                                switch (_stage._overflowStrategy)
                                {
                                    case OverflowStrategy.DropHead:
                                    case OverflowStrategy.DropBuffer:
                                        _pendingOffer.CompletionSource.NonBlockingTrySetResult(QueueOfferResult.Dropped.Instance);
                                        _pendingOffer = offer;
                                        break;
                                    case OverflowStrategy.DropTail:
                                    case OverflowStrategy.DropNew:
                                        offer.CompletionSource.NonBlockingTrySetResult(QueueOfferResult.Dropped.Instance);
                                        break;
                                    case OverflowStrategy.Backpressure:
                                        offer.CompletionSource.NonBlockingTrySetException(
                                            new IllegalStateException(
                                                "You have to wait for previous offer to be resolved to send another request"));
                                        break;
                                    case OverflowStrategy.Fail:
                                        var bufferOverflowException =
                                            new BufferOverflowException(
                                                $"Buffer overflow (max capacity was: {_stage._maxBuffer})!");
                                        offer.CompletionSource.NonBlockingTrySetResult(new QueueOfferResult.Failure(bufferOverflowException));
                                        _completion.SetException(bufferOverflowException);
                                        FailStage(bufferOverflowException);
                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                        }

                        var completion = input as Completion;
                        if (completion != null)
                        {
                            if (_stage._maxBuffer != 0 && _buffer.NonEmpty || _pendingOffer != null)
                                _terminating = true;
                            else
                            {
                                _completion.SetResult(new object());
                                CompleteStage();
                            }
                        }

                        var failure = input as Failure;
                        if (failure != null)
                        {
                            _completion.SetException(failure.Ex);
                            FailStage(failure.Ex);
                        }
                    });
            }

            internal void Invoke(IInput offer) => InvokeCallbacks(offer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Materialized : ISourceQueueWithComplete<TOut>
        {
            private readonly Action<IInput> _invokeLogic;
            private readonly TaskCompletionSource<object> _completion;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="invokeLogic">TBD</param>
            /// <param name="completion">TBD</param>
            public Materialized(Action<IInput> invokeLogic, TaskCompletionSource<object> completion)
            {
                _invokeLogic = invokeLogic;
                _completion = completion;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
            /// <returns>TBD</returns>
            public Task<IQueueOfferResult> OfferAsync(TOut element)
            {
                var promise = TaskEx.NonBlockingTaskCompletionSource<IQueueOfferResult>(); // new TaskCompletionSource<IQueueOfferResult>();
                _invokeLogic(new Offer<TOut>(element, promise));
                return promise.Task;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public Task WatchCompletionAsync() => _completion.Task;

            /// <summary>
            /// TBD
            /// </summary>
            public void Complete() => _invokeLogic(Completion.Instance);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ex">TBD</param>
            public void Fail(Exception ex) => _invokeLogic(new Failure(ex));
        }

        private readonly int _maxBuffer;
        private readonly OverflowStrategy _overflowStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxBuffer">TBD</param>
        /// <param name="overflowStrategy">TBD</param>
        public QueueSource(int maxBuffer, OverflowStrategy overflowStrategy)
        {
            _maxBuffer = maxBuffer;
            _overflowStrategy = overflowStrategy;
            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("queueSource.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<ISourceQueueWithComplete<TOut>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<object>();
            var logic = new Logic(this, completion);
            return new LogicAndMaterializedValue<ISourceQueueWithComplete<TOut>>(logic, new Materialized(t => logic.Invoke(t), completion));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class UnfoldResourceSource<TOut, TSource> : GraphStage<SourceShape<TOut>>
    {
        #region Logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldResourceSource<TOut, TSource> _stage;
            private readonly Lazy<Decider> _decider;
            private TSource _blockingStream;
            private bool _open;

            public Logic(UnfoldResourceSource<TOut, TSource> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _decider = new Lazy<Decider>(() =>
                {
                    var strategy = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return strategy != null ? strategy.Decider : Deciders.StoppingDecider;
                });

                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                var stop = false;
                while (!stop)
                {
                    try
                    {
                        var data = _stage._readData(_blockingStream);
                        if (data.HasValue)
                            Push(_stage.Out, data.Value);
                        else
                            CloseStage();

                        break;
                    }
                    catch (Exception ex)
                    {
                        var directive = _decider.Value(ex);
                        switch (directive)
                        {
                            case Directive.Stop:
                                _open = false;
                                _stage._close(_blockingStream);
                                FailStage(ex);
                                stop = true;
                                break;
                            case Directive.Restart:
                                RestartState();
                                break;
                            case Directive.Resume:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }

            public override void OnDownstreamFinish() => CloseStage();

            public override void PreStart()
            {
                _blockingStream = _stage._create();
                _open = true;
            }

            private void RestartState()
            {
                _open = false;
                _stage._close(_blockingStream);
                _blockingStream = _stage._create();
                _open = true;
            }

            private void CloseStage()
            {
                try
                {
                    _stage._close(_blockingStream);
                    _open = false;
                    CompleteStage();
                }
                catch (Exception ex)
                {
                    FailStage(ex);
                }
            }

            public override void PostStop()
            {
                if (_open)
                    _stage._close(_blockingStream);
            }
        }

        #endregion

        private readonly Func<TSource> _create;
        private readonly Func<TSource, Option<TOut>> _readData;
        private readonly Action<TSource> _close;


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="create">TBD</param>
        /// <param name="readData">TBD</param>
        /// <param name="close">TBD</param>
        public UnfoldResourceSource(Func<TSource> create, Func<TSource, Option<TOut>> readData, Action<TSource> close)
        {
            _create = create;
            _readData = readData;
            _close = close;

            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.UnfoldResourceSource;

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("UnfoldResourceSource.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "UnfoldResourceSource";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TSource">TBD</typeparam>
    [InternalApi]
    public sealed class UnfoldResourceSourceAsync<TOut, TSource> : GraphStage<SourceShape<TOut>>
    {
        #region Logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldResourceSourceAsync<TOut, TSource> _stage;
            private readonly Lazy<Decider> _decider;
            private Option<TSource> _state = Option<TSource>.None;

            public Logic(UnfoldResourceSourceAsync<TOut, TSource> stage, Attributes inheritedAttributes)
                : base(stage.Shape)
            {
                _stage = stage;
                _decider = new Lazy<Decider>(() =>
                {
                    var strategy = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return strategy != null ? strategy.Decider : Deciders.StoppingDecider;
                });

                SetHandler(_stage.Out, this);
            }

            private Action<Try<TSource>> CreatedCallback => GetAsyncCallback<Try<TSource>>(resource =>
            {
                if (resource.IsSuccess)
                {
                    _state = resource.Success;
                    if (IsAvailable(_stage.Out)) OnPull();
                }
                else FailStage(resource.Failure.Value);
            });

            private void ErrorHandler(Exception ex)
            {
                switch (_decider.Value(ex))
                {
                    case Directive.Stop:
                        FailStage(ex);
                        break;
                    case Directive.Restart:
                        try
                        {
                            RestartResource();
                        }
                        catch (Exception ex1)
                        {
                            FailStage(ex1);
                        }
                        break;
                    case Directive.Resume:
                        OnPull();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private Action<Try<Option<TOut>>> ReadCallback => GetAsyncCallback<Try<Option<TOut>>>(read =>
            {
                if (read.IsSuccess)
                {
                    var data = read.Success.Value;
                    if (data.HasValue)
                    {
                        var some = data.Value;
                        Push(_stage.Out, some);
                    }
                    else
                    {
                        // end of resource reached, lets close it
                        if (_state.HasValue)
                        {
                            var resource = _state.Value;
                            _stage._close(resource).OnComplete(GetAsyncCallback<Try<Done>>(done =>
                            {
                                if (done.IsSuccess) CompleteStage();
                                else FailStage(done.Failure.Value);
                            }));
                            _state = Option<TSource>.None;
                        }
                        else
                        {
                            // cannot happen, but for good measure
                            throw new InvalidOperationException("Reached end of data but there is no open resource");
                        }
                    }
                }
                else ErrorHandler(read.Failure.Value);
            });

            public override void PreStart() => CreateResource();

            public override void OnPull()
            {
                if (_state.HasValue)
                {
                    try
                    {
                        var resource = _state.Value;
                        _stage._readData(resource).OnComplete(ReadCallback);
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler(ex);
                    }
                }
                else
                {
                    // we got a pull but there is no open resource, we are either
                    // currently creating/restarting then the read will be triggered when creating the
                    // resource completes, or shutting down and then the pull does not matter anyway
                }
            }

            public override void PostStop()
            {
                if (_state.HasValue)
                    _stage._close(_state.Value);
            }

            private void RestartResource()
            {
                if (_state.HasValue)
                {
                    var resource = _state.Value;
                    // wait for the resource to close before restarting
                    _stage._close(resource).OnComplete(GetAsyncCallback<Try<Done>>(done =>
                    {
                        if (done.IsSuccess) CreateResource();
                        else FailStage(done.Failure.Value);
                    }));
                    _state = Option<TSource>.None;
                }
                else CreateResource();
            }

            private void CreateResource()
            {
                _stage._create().OnComplete(resource =>
                {
                    try
                    {
                        CreatedCallback(resource);
                    }
                    catch (StreamDetachedException)
                    {
                        // stream stopped before created callback could be invoked, we need
                        // to close the resource if it is was opened, to not leak it
                        if (resource.IsSuccess)
                        {
                            _stage._close(resource.Success.Value);
                        }
                        else
                        {
                            // failed to open but stream is stopped already
                            throw resource.Failure.Value;
                        }
                    }
                });
            }
        }

        #endregion

        private readonly Func<Task<TSource>> _create;
        private readonly Func<TSource, Task<Option<TOut>>> _readData;
        private readonly Func<TSource, Task> _close;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="create">TBD</param>
        /// <param name="readData">TBD</param>
        /// <param name="close">TBD</param>
        public UnfoldResourceSourceAsync(Func<Task<TSource>> create, Func<TSource, Task<Option<TOut>>> readData, Func<TSource, Task> close)
        {
            _create = create;
            _readData = readData;
            _close = close;

            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes => DefaultAttributes.UnfoldResourceSourceAsync;

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("UnfoldResourceSourceAsync.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "UnfoldResourceSourceAsync";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class LazySource<TOut, TMat> : GraphStageWithMaterializedValue<SourceShape<TOut>, Task<TMat>>
    {
        #region Logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly LazySource<TOut, TMat> _stage;
            private readonly TaskCompletionSource<TMat> _completion;
            private readonly Attributes _inheritedAttributes;

            public Logic(LazySource<TOut, TMat> stage, TaskCompletionSource<TMat> completion, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _completion = completion;
                _inheritedAttributes = inheritedAttributes;

                SetHandler(stage.Out, this);
            }

            public override void OnDownstreamFinish()
            {
                _completion.SetException(new Exception("Downstream canceled without triggering lazy source materialization"));
                CompleteStage();
            }


            public override void OnPull()
            {
                var source = _stage._sourceFactory();
                var subSink = new SubSinkInlet<TOut>(this, "LazySource");
                subSink.Pull();

                SetHandler(_stage.Out, () => subSink.Pull(), () =>
                {
                    subSink.Cancel();
                    CompleteStage();
                });

                subSink.SetHandler(new LambdaInHandler(() => Push(_stage.Out, subSink.Grab())));

                try
                {
                    var value = SubFusingMaterializer.Materialize(source.ToMaterialized(subSink.Sink, Keep.Left),
                        _inheritedAttributes);
                    _completion.SetResult(value);
                }
                catch (Exception e)
                {
                    subSink.Cancel();
                    FailStage(e);
                    _completion.TrySetException(e);
                }
            }

            public override void PostStop() => _completion.TrySetException(
                new Exception("LazySource stopped without completing the materialized task"));
        }

        #endregion

        private readonly Func<Source<TOut, TMat>> _sourceFactory;

        /// <summary>
        /// Creates a new <see cref="LazySource{TOut,TMat}"/>
        /// </summary>
        /// <param name="sourceFactory">The factory that generates the source when needed</param>
        public LazySource(Func<Source<TOut, TMat>> sourceFactory)
        {
            _sourceFactory = sourceFactory;
            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("LazySource.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.LazySource;

        /// <summary>
        /// TBD
        /// </summary>
        public override ILogicAndMaterializedValue<Task<TMat>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<TMat>();
            var logic = new Logic(this, completion, inheritedAttributes);

            return new LogicAndMaterializedValue<Task<TMat>>(logic, completion.Task);
        }

        /// <summary>
        /// Returns the string representation of the <see cref="LazySource{TOut,TMat}"/>
        /// </summary>
        public override string ToString() => "LazySource";
    }

    /// <summary>
    /// API for the <see cref="LazySource{TOut,TMat}"/>
    /// </summary>
    public static class LazySource
    {
        /// <summary>
        /// Creates a new <see cref="LazySource{TOut,TMat}"/> for the given <paramref name="create"/> factory
        /// </summary>
        public static LazySource<TOut, TMat> Create<TOut, TMat>(Func<Source<TOut, TMat>> create) =>
            new LazySource<TOut, TMat>(create);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class EmptySource<TOut> : GraphStage<SourceShape<TOut>>
    {
        private sealed class Logic : OutGraphStageLogic
        {
            public Logic(EmptySource<TOut> stage) : base(stage.Shape) => SetHandler(stage.Out, this);

            public override void OnPull() => CompleteStage();

            public override void PreStart() => CompleteStage();
        }

        public EmptySource()
        {
            Shape = new SourceShape<TOut>(Out);
        }

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("EmptySource.out");

        public override SourceShape<TOut> Shape { get; }

        protected override Attributes InitialAttributes => DefaultAttributes.LazySource;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "EmptySource";
    }

    internal sealed class EventWrapper<TDelegate, TEventArgs> : IObservable<TEventArgs>
    {
        #region disposer

        private class Disposer : IDisposable
        {
            private readonly EventWrapper<TDelegate, TEventArgs> _observable;
            private readonly TDelegate _handler;

            public Disposer(EventWrapper<TDelegate, TEventArgs> observable, TDelegate handler)
            {
                _observable = observable;
                _handler = handler;
            }

            public void Dispose()
            {
                _observable._removeHandler(_handler);
            }
        }

        #endregion

        private readonly Action<TDelegate> _addHandler;
        private readonly Action<TDelegate> _removeHandler;
        private readonly Func<Action<TEventArgs>, TDelegate> _conversion;

        /// <summary>
        /// Creates a new instance of EventWrapper - an object wrapping C# events with an observable object.
        /// </summary>
        /// <param name="conversion">Function used to convert given event handler to delegate compatible with underlying .NET event.</param>
        /// <param name="addHandler">Action which attaches given event handler to the underlying .NET event.</param>
        /// <param name="removeHandler">Action which detaches given event handler to the underlying .NET event.</param>
        public EventWrapper(Action<TDelegate> addHandler, Action<TDelegate> removeHandler, Func<Action<TEventArgs>, TDelegate> conversion)
        {
            _addHandler = addHandler;
            _removeHandler = removeHandler;
            _conversion = conversion;
        }

        public IDisposable Subscribe(IObserver<TEventArgs> observer)
        {
            var handler = _conversion(observer.OnNext);
            _addHandler(handler);
            return new Disposer(this, handler);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class ObservableSourceStage<T> : GraphStage<SourceShape<T>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic, IObserver<T>
        {
            private readonly ObservableSourceStage<T> _stage;
            private readonly LinkedList<T> _buffer;
            private readonly Action<T> _onOverflow;
            private readonly Action<T> _onEvent;
            private readonly Action<Exception> _onError;
            private readonly Action _onCompleted;

            private IDisposable _disposable;

            public Logic(ObservableSourceStage<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _buffer = new LinkedList<T>();
                var bufferCapacity = stage._maxBufferCapacity;
                _onEvent = GetAsyncCallback<T>(e =>
                {
                    if (IsAvailable(_stage.Outlet))
                    {
                        Push(_stage.Outlet, e);
                    }
                    else
                    {
                        if (_buffer.Count >= bufferCapacity) _onOverflow(e);
                        else Enqueue(e);
                    }
                });
                _onError = GetAsyncCallback<Exception>(e => Fail(_stage.Outlet, e));
                _onCompleted = GetAsyncCallback(() => Complete(_stage.Outlet));
                _onOverflow = SetupOverflowStrategy(stage._overflowStrategy);

                SetHandler(stage.Outlet, onPull: () =>
                {
                    if (_buffer.Count > 0)
                    {
                        var element = Dequeue();
                        Push(_stage.Outlet, element);
                    }
                }, onDownstreamFinish: OnCompleted);
            }

            public void OnNext(T value) => _onEvent(value);
            public void OnError(Exception error) => _onError(error);
            public void OnCompleted() => _onCompleted();

            public override void PreStart()
            {
                base.PreStart();
                _disposable = _stage._observable.Subscribe(this);
            }

            public override void PostStop()
            {
                _disposable?.Dispose();
                _buffer.Clear();
                base.PostStop();
            }

            private void Enqueue(T e) => _buffer.AddLast(e);

            private T Dequeue()
            {
                var element = _buffer.First.Value;
                _buffer.RemoveFirst();
                return element;
            }

            private Action<T> SetupOverflowStrategy(OverflowStrategy overflowStrategy)
            {
                switch (overflowStrategy)
                {
                    case OverflowStrategy.DropHead:
                        return message =>
                        {
                            _buffer.RemoveFirst();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropTail:
                        return message =>
                        {
                            _buffer.RemoveLast();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropNew:
                        return message =>
                        {
                            /* do nothing */
                        };
                    case OverflowStrategy.DropBuffer:
                        return message =>
                        {
                            _buffer.Clear();
                            Enqueue(message);
                        };
                    case OverflowStrategy.Fail:
                        return message => FailStage(new BufferOverflowException($"{_stage.Outlet} buffer has been overflown"));
                    case OverflowStrategy.Backpressure:
                        return message => throw new NotSupportedException("OverflowStrategy.Backpressure is not supported");
                    default: throw new NotSupportedException($"Unknown option: {overflowStrategy}");
                }
            }
        }

        #endregion

        private readonly IObservable<T> _observable;
        private readonly int _maxBufferCapacity;
        private readonly OverflowStrategy _overflowStrategy;

        public ObservableSourceStage(IObservable<T> observable, int maxBufferCapacity, OverflowStrategy overflowStrategy)
        {
            _observable = observable;
            _maxBufferCapacity = maxBufferCapacity;
            _overflowStrategy = overflowStrategy;

            Shape = new SourceShape<T>(Outlet);
        }

        public Outlet<T> Outlet { get; } = new Outlet<T>("observable.out");
        public override SourceShape<T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}