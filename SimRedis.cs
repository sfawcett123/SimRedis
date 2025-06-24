using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using StackExchange.Redis;


namespace SimRedis
{
    public class RedisDataEventArgs : EventArgs
    {
        public Dictionary<string,string> AircraftData = new Dictionary<string,string>();
    }
    public class SimRedis : IDisposable
    {// EventLogSettings is used to configure the event log settings for the logger
        private static EventLogSettings myEventLogSettings = new EventLogSettings
        {
            SourceName = _sourceName,
            LogName = _logName
        };
        private const string _sourceName = "Simulator Service";
        private const string _logName = "Application";
        private ConnectionMultiplexer? _redis;
        private IDatabase? db = null;
        private ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.AddEventLog(myEventLogSettings);
        });
        private ILogger? logger = null;
        private System.Timers.Timer? connectionTimer , readTimer;

        private const int CONNECT_TIME = 5000;
        private const int REDIS_TIMEOUT = 50000;

        public int port
        {
            get
            {
                return _port;
            }
            set
            {
                if (value > 0 && value < 65536)
                {
                    _port = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Port number must be between 1 and 65535.");
                }
            }
        }       
        public string server
        {
            get
            {
                return _server;
            }
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _server = value;
                }
                else
                {
                    throw new ArgumentException("Server name cannot be null or empty.", nameof(value));
                }
            }
        }
        public int READTIME
        {
            get
            {
                return (int)(readTimer?.Interval ?? 100); // Explicitly cast 'double' to 'int'
            }
            set
            {
                if (readTimer != null)
                {
                    readTimer.Interval = value;
                }
            }
        }
        public bool Connected
        {
            get
            {
                // Check if the Redis connection is established
                return _redis != null && _redis.IsConnected && db != null;
            }
        }

        // Fix for CS8050: Move the initializer to the constructor
        private int _readTime = 100; // Backing field for READTIME
        private int _port = 6379;
        private string _server = "localhost";

        public event EventHandler<RedisDataEventArgs>? RedisDataRecieved;
        public bool Enabled
        {
            get
            {
                return connectionTimer?.Enabled ?? false;
            }
            set
            {
                if( value )
                {
                    if (connectionTimer == null)
                    {
                        connectionTimer = new System.Timers.Timer(CONNECT_TIME);
                        connectionTimer.Elapsed += onConnected;
                    }
                    connectionTimer.Start();
                }
                else
                {
                    connectionTimer?.Stop();
                    readTimer?.Stop();
                }
            }
        }
        public void Dispose()
        {
            // Add cleanup logic here if necessary
            _redis?.Dispose();
            db = null;
        }
        public SimRedis()
        {
            // Default constructor that initializes the logger and connects to Redis
            Yaml r = new Yaml(Path.Combine(Environment.CurrentDirectory, "settings", "redis.yaml"));
            server = r.server ?? "localhost";

           if (!int.TryParse(r.port.ToString(), out _port))
            {
                port = 6379; // Default port if parsing fails
            }

            Initialize();
            READTIME = _readTime; // Set the initial read time
        }
        public SimRedis(string server, int port, ILoggerFactory loggerFactory)
        {
            // Default constructor that initializes the logger and connects to Redis
            this.factory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory), "Logger factory cannot be null.");
            Initialize();
            READTIME = _readTime; // Set the initial read time
        }
        public void Initialize()
        {
            this.logger = factory.CreateLogger("Redis Listener");

            connectionTimer = new System.Timers.Timer()
            {
                Enabled = true,
                AutoReset = true,
                Interval = CONNECT_TIME
            };
            connectionTimer.Start();
            connectionTimer.Elapsed += onConnected;
            logger?.LogInformation($"Waiting for connection to Redis at {this.server}:{this.port}");

            readTimer = new System.Timers.Timer()
            {
                Enabled = true,
                AutoReset = true,
                Interval = READTIME
            };
            readTimer.Stop();
            readTimer.Elapsed += onGetData;
        }
        protected virtual void OnRedisData(RedisDataEventArgs e)
        {
            EventHandler<RedisDataEventArgs>? handler = RedisDataRecieved;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        private void onGetData( object? sender , System.Timers.ElapsedEventArgs e )
        {
            if( sender  == null ) return;
            
            try
            {
                RedisDataEventArgs args = new RedisDataEventArgs();
                if (_redis == null || db == null)
                {
                    logger?.LogInformation("Internal Database not available");
                    throw new InvalidOperationException("Redis connection is not established.");
                }

                foreach (var endpoint in _redis.GetEndPoints() )
                {
                    var server = _redis.GetServer(endpoint);
                    logger?.LogDebug($"Connected to Redis server at {server.EndPoint}");
                    foreach (var key in server.Keys())
                    {
                        logger?.LogDebug($"Reading key: {key}");
                        string? value = db.StringGet(key);
                        if (value != null)
                        {
                            args.AircraftData.Add(key.ToString() ?? string.Empty, value);
                        }
                    }
                }
                OnRedisData(args);

            }
            catch {
                logger?.LogWarning("Redis connection lost");
                this.readTimer?.Stop();
                this.connectionTimer?.Start();
            }

        }
        private void onConnected(object? sender, System.Timers.ElapsedEventArgs e)
        {
            int port = 0;

            if (sender?.GetType() != typeof(System.Timers.Timer))
            {
                logger?.LogWarning("Sender is not of type Timer.");
                return;
            }

            System.Timers.Timer timer = (System.Timers.Timer)sender ?? throw new ArgumentNullException(nameof(sender), "Sender cannot be null.");
            if (timer != connectionTimer)
            {
                logger?.LogWarning("Unexpected timer event received.");
                return;
            }

            try
            {
                port = this.port;
            }
            catch (Exception ex)
            {
                logger?.LogError($"{ex.Message} - Cannot convert {this.port} to int");
                return;
            }

            try
            {
                if (db == null)
                {
                    logger?.LogInformation($"Connecting to Redis at {this.server}:{port}");
                    _redis = ConnectionMultiplexer.Connect($"{this.server}:{port},ConnectTimeout={REDIS_TIMEOUT}");
                    
                    db = _redis.GetDatabase();
                    if (db == null)
                    {
                        logger?.LogError("Failed to connect to Redis database.");
                        return;
                    }
                    logger?.LogInformation($"Connected to Redis at {this.server}:{port}");
                    readTimer?.Start();
                    if (connectionTimer != null)
                    {
                        logger?.LogInformation($"Stopping Connection Timer");
                        connectionTimer.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogInformation($"Redis Not available {ex.Message}");
                connectionTimer.Start();
                readTimer?.Stop();
                return;
            }
        }
        public void write( string key , string value )
        {
            if (!Connected)
            {
                logger?.LogInformation("Unable to Write. Database not available");
                return;
            }

            logger?.LogInformation($"Writing {key} = {value} to Redis");
            db?.StringSet(key, value);
        }
    }
}
