namespace AgenticAIAPI.Infra
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MCPAttribute : Attribute
    {
        public string Intent { get; }

        public MCPAttribute(string intent)
        {
            Intent = intent;
        }
    }

}
