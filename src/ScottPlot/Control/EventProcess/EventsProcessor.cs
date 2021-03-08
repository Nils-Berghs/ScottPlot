﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ScottPlot.Control.EventProcess
{
    /// <summary>
    /// This event processor process incoming events and invokes renders as needed.
    /// This class contains logic to optionally display a fast preview render and a delayed high quality render.
    /// </summary>
    public class EventsProcessor
    {
        private readonly Queue<IUIEvent> Queue = new Queue<IUIEvent>();
        private bool QueueHasUnprocessedEvents => Queue.Count > 0;
        private bool QueueIsEmpty => Queue.Count == 0;

        /// <summary>
        /// This timer is used for delayed rendering.
        /// It is restarted whenever an event is processed which requests a delayed render.
        /// </summary>
        private readonly Stopwatch RenderDelayTimer = new Stopwatch();

        /// <summary>
        /// This is true while the processor is processing events and/or waiting for a delayed render.
        /// </summary>
        private bool QueueProcessorIsRunning = false;

        /// <summary>
        /// Time to wait after a low-quality render to invoke a high quality render
        /// </summary>
        public int RenderDelayMilliseconds { get; set; }

        /// <summary>
        /// When a render is required this Action will be invoked.
        /// Its argument indicates whether low quality should be used.
        /// </summary>
        public Action<bool> RenderAction { get; set; }

        /// <summary>
        /// Create a processor to invoke renders in response to incoming events
        /// </summary>
        /// <param name="renderAction">Action to invoke to perform a render. Bool argument is LowQuality.</param>
        /// <param name="renderDelay">Milliseconds after low-quality render to re-render using high quality.</param>
        public EventsProcessor(Action<bool> renderAction, int renderDelay)
        {
            RenderAction = renderAction;
            RenderDelayMilliseconds = renderDelay;
        }

        /// <summary>
        /// Perform a high quality render.
        /// Call this instead of the action itself because this has better-documented arguments.
        /// </summary>
        private void RenderHighQuality() => RenderAction.Invoke(false);

        /// <summary>
        /// Perform a low quality render.
        /// Call this instead of the action itself because this has better-documented arguments.
        /// </summary>
        private void RenderLowQuality() => RenderAction.Invoke(true);

        /// <summary>
        /// Add an event to the queue and process it when it is ready.
        /// After all events are processed a render will be called automatically.
        /// </summary>
        public async Task Process(IUIEvent uiEvent)
        {
            Queue.Enqueue(uiEvent);

            // start a new processor only if one is not already running
            if (!QueueProcessorIsRunning)
                await RunQueueProcessor();
        }

        /// <summary>
        /// Perform a low quality preview render if the render type allows it
        /// </summary>
        private void RenderPreview(RenderType renderType)
        {
            if (renderType == RenderType.HQOnly)
                return;

            RenderLowQuality();
        }

        /// <summary>
        /// Perform a final high quality render if the render type allows it.
        /// </summary>
        /// <returns>Return False if the final render needs to happen later</returns>
        private bool RenderFinal(RenderType renderType)
        {
            switch (renderType)
            {
                case RenderType.LQOnly:
                    // we don't need a HQ render if the type is LQ only
                    return true;

                case RenderType.HQOnly:
                    // A HQ version is always needed
                    RenderHighQuality();
                    return true;

                case RenderType.HQAfterLQImmediately:
                    // A LQ version has been rendered, but we need a HQ version now
                    RenderHighQuality();
                    return true;

                case RenderType.HQAfterLQDelayed:
                    // A LQ version has been rendered, but we need a HQ version after a delay

                    if (RenderDelayTimer.IsRunning == false)
                        RenderDelayTimer.Restart();

                    if (RenderDelayTimer.ElapsedMilliseconds > RenderDelayMilliseconds)
                    {
                        RenderHighQuality();
                        RenderDelayTimer.Stop();
                        return true;
                    }
                    else
                    {
                        return false;
                    }

                default:
                    throw new ArgumentException($"Unknown quality: {renderType}");
            }
        }

        /// <summary>
        /// Process every event in the queue.
        /// A render will be executed after each event is processed.
        /// A slight delay will be added between queue checks.
        /// </summary>
        private async Task RunQueueProcessor()
        {
            RenderType lastEventRenderType = RenderType.None;
            while (true)
            {
                QueueProcessorIsRunning = true;
                bool eventRenderRequested = false;
                while (QueueHasUnprocessedEvents)
                {
                    var uiEvent = Queue.Dequeue();
                    uiEvent.ProcessEvent();

                    if (uiEvent.RenderOrder == RenderType.HQAfterLQDelayed)
                        RenderDelayTimer.Restart();

                    if (uiEvent.RenderOrder != RenderType.None)
                    {
                        lastEventRenderType = uiEvent.RenderOrder;
                        eventRenderRequested = true;
                    }
                }

                if (eventRenderRequested)
                    RenderPreview(lastEventRenderType);

                // TODO: how small can this number be?
                await Task.Delay(1);

                // If new events came in, handle those instead of proceeding toward a final render
                if (QueueHasUnprocessedEvents)
                    continue;

                // Perform the final render and shut down the processor loop
                bool finalRenderWasPerformed = RenderFinal(lastEventRenderType);
                if (finalRenderWasPerformed)
                    break;
            };

            QueueProcessorIsRunning = false;
        }
    }
}
