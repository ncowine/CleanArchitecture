using System;
using Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Prism.Events;

namespace Messaging.Prism
{
    public static class MessagingBuilderExtensions
    {
        /// <summary>
        /// Raises every received message as <see cref="MessageEvent{TMessage}"/> on <paramref name="eventAggregator"/>, the
        /// application's Prism aggregator. Messages arrive for the types added with <see cref="MessagingBuilder.AddMessages"/>
        /// or <see cref="MessagingBuilder.Handle{TMessage, THandler}"/>.
        /// </summary>
        public static MessagingBuilder UseEventAggregator(this MessagingBuilder builder, IEventAggregator eventAggregator)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (eventAggregator == null)
            {
                throw new ArgumentNullException(nameof(eventAggregator));
            }

            builder.Services.AddSingleton<IMessageSink>(new EventAggregatorSink(eventAggregator));
            return builder;
        }
    }
}
