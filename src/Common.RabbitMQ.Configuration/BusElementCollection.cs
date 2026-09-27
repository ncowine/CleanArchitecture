using System.Configuration;

namespace Common.RabbitMQ.Configuration
{
    public class BusElementCollection : ConfigurationElementCollection
    {
        public override ConfigurationElementCollectionType CollectionType => ConfigurationElementCollectionType.BasicMap;

        protected override string ElementName => "bus";

        protected override ConfigurationElement CreateNewElement()
        {
            return new BusElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((BusElement)element).Name;
        }
    }
}
