using System;
using System.Collections.Generic;
using MessagePipe;

namespace MustyBlockBlast.Tests.PlayMode
{
    /// <summary>
    /// Minimal in-test stand-in for MessagePipe's <c>MessageBroker{TMessage}</c>: publishes
    /// synchronously to every subscriber, so a test can construct a System without standing up a
    /// container. A PlayMode-local twin of <c>MustyBlockBlast.Tests.EditMode.TestMessageBroker</c> —
    /// that one is <c>internal</c> to the EditMode assembly and this assembly cannot reference it.
    /// </summary>
    internal sealed class TestMessageBroker<TMessage> : IPublisher<TMessage>, ISubscriber<TMessage>
    {
        private readonly List<IMessageHandler<TMessage>> _handlers = new List<IMessageHandler<TMessage>>();

        public void Publish(TMessage message)
        {
            // Copied because a handler may unsubscribe while being invoked.
            var snapshot = new List<IMessageHandler<TMessage>>(_handlers);
            for (int i = 0; i < snapshot.Count; i++)
            {
                snapshot[i].Handle(message);
            }
        }

        public IDisposable Subscribe(IMessageHandler<TMessage> handler, params MessageHandlerFilter<TMessage>[] filters)
        {
            // Filters are unused on purpose: no production code under test attaches any, and honouring
            // them would mean reimplementing MessagePipe rather than standing in for it.
            _handlers.Add(handler);
            return new Subscription(this, handler);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly TestMessageBroker<TMessage> _broker;
            private readonly IMessageHandler<TMessage> _handler;

            internal Subscription(TestMessageBroker<TMessage> broker, IMessageHandler<TMessage> handler)
            {
                _broker = broker;
                _handler = handler;
            }

            public void Dispose() => _broker._handlers.Remove(_handler);
        }
    }
}
