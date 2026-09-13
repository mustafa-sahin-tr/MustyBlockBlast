using System;
using System.Collections.Generic;
using MessagePipe;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Minimal in-test stand-in for MessagePipe's <see cref="MessageBroker{TMessage}"/>: publishes
    /// synchronously to every subscriber and records everything published, so a test can assert on the
    /// messages a System emitted without standing up a container.
    /// </summary>
    internal sealed class TestMessageBroker<TMessage> : IPublisher<TMessage>, ISubscriber<TMessage>
    {
        private readonly List<IMessageHandler<TMessage>> _handlers = new List<IMessageHandler<TMessage>>();
        private readonly List<TMessage> _published = new List<TMessage>();

        /// <summary>Every message published through this broker, in order.</summary>
        internal IReadOnlyList<TMessage> Published => _published;

        public void Publish(TMessage message)
        {
            _published.Add(message);

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
