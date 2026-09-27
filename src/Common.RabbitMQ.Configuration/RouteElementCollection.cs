using System.Configuration;

namespace Common.RabbitMQ.Configuration
{
    [ConfigurationCollection(typeof(RouteElement), AddItemName = "route", CollectionType = ConfigurationElementCollectionType.BasicMap)]
    public class RouteElementCollection : ConfigurationElementCollection
    {
        public override ConfigurationElementCollectionType CollectionType => ConfigurationElementCollectionType.BasicMap;

        protected override string ElementName => "route";

        protected override ConfigurationElement CreateNewElement()
        {
            return new RouteElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((RouteElement)element).Event;
        }
    }
}
