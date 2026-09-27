using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Common.RabbitMQ.Configuration
{
    public static class RabbitMQConfigLoader
    {
        public const string DefaultSectionName = "rabbitMQ";

        /// <summary>Reads every &lt;bus&gt; from the &lt;rabbitMQ&gt; section of App.config.</summary>
        public static IReadOnlyList<RabbitMQConfig> Load(string sectionName = DefaultSectionName)
        {
            RabbitMQConfigSection section = ConfigurationManager.GetSection(sectionName) as RabbitMQConfigSection;
            if (section == null)
            {
                throw new ConfigurationErrorsException($"App.config has no <{sectionName}> section of type {typeof(RabbitMQConfigSection).FullName}.");
            }

            return section.Buses
                .Cast<BusElement>()
                .Select(bus => bus.ToRabbitMQConfig())
                .ToList();
        }
    }
}
