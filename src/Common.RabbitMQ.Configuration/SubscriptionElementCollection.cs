using System.Configuration;

namespace Common.RabbitMQ.Configuration
{
    [ConfigurationCollection(typeof(SubscriptionElement), AddItemName = "subscribe", CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class SubscriptionElementCollection : ConfigurationElementCollection
    {
        public override ConfigurationElementCollectionType CollectionType => ConfigurationElementCollectionType.BasicMap;

        protected override string ElementName => "subscribe";

        protected override ConfigurationElement CreateNewElement()
        {
            return new SubscriptionElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((SubscriptionElement)element).Exchange;
        }
    }
}
