using System.Diagnostics;
using YamlDotNet.Serialization;

namespace SimRedis
{ 
    internal class Yaml
    {
        public int port
        {
            get
            {
                if (data == null)
                {
                    return 6379;
                }
                DataDefinition d = (DataDefinition)data;
                return d?.port ?? 6379;
            }
        }
        public string server
        {
            get
            {
                if(data == null)
                {
                    return "controller.local";
                }
                DataDefinition d = (DataDefinition)data;
                return d?.server ?? "controller.local";
            }
        }
        public DataDefinition data { get; private set; }
        public class DataDefinition
        {
            public string server { get; set; } = "controller.local";
            public int port { get; set; } = 6379;
        }
        public Yaml(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The specified YAML file does not exist.", filePath);
            }
            // Load the YAML file
            string yaml = File.ReadAllText(filePath);

            data = Deserializer(yaml);

        }
        public DataDefinition Deserializer( string Data )
        {
           var deserializer = new DeserializerBuilder()
                .WithCaseInsensitivePropertyMatching()
                .Build();

           return deserializer.Deserialize<DataDefinition>(Data.ToString());
        }
    }
}
