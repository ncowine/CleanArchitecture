namespace Common.Events
{
    /// <summary>Bus names; must match the bus names in the application configs.</summary>
    public static class BusNames
    {
        /// <summary>net472 apps talk to each other here. The net8 app and the API join it too.</summary>
        public const string Legacy = "Legacy";

        /// <summary>net8 apps and the API.</summary>
        public const string Modern = "Modern";
    }
}
