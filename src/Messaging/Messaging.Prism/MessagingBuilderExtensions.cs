using System;
using Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prism.Events;

namespace Messaging.Prism
{
    public static class MessagingBuilderExtensions
    {
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
            builder.Services.AddSingleton<IHostedService>(provider => new MessageEventReceiver(provider.GetRequiredService<IMessageSubscriber>(), eventAggregator));
            return builder;
        }
    }
}
