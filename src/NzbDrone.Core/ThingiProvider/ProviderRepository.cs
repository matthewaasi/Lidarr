using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Reflection;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ThingiProvider
{
    public class ProviderRepository<TProviderDefinition> : BasicRepository<TProviderDefinition>, IProviderRepository<TProviderDefinition>
        where TProviderDefinition : ProviderDefinition,
            new()
    {
        private static readonly ConcurrentDictionary<string, byte> ReportedUnknownProviders = new ConcurrentDictionary<string, byte>();
        protected readonly JsonSerializerOptions _serializerSettings;
        private readonly Logger _logger;

        protected ProviderRepository(IMainDatabase database,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;

            var serializerSettings = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            serializerSettings.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true));
            serializerSettings.Converters.Add(new STJTimeSpanConverter());
            serializerSettings.Converters.Add(new STJUtcConverter());

            _serializerSettings = serializerSettings;
        }

        protected override List<TProviderDefinition> Query(SqlBuilder builder)
        {
            var type = typeof(TProviderDefinition);
            var sql = builder.Select(type).AddSelectTemplate(type);

            var results = new List<TProviderDefinition>();

            using (var conn = _database.OpenConnection())
            using (var reader = conn.ExecuteReader(sql.RawSql, sql.Parameters))
            {
                var parser = reader.GetRowParser<TProviderDefinition>(typeof(TProviderDefinition));
                var settingsIndex = reader.GetOrdinal(nameof(ProviderDefinition.Settings));

                while (reader.Read())
                {
                    var body = reader.IsDBNull(settingsIndex) ? null : reader.GetString(settingsIndex);
                    var item = parser(reader);
                    var impType = ReflectionExtensions.FindTypeByName(item.ConfigContract);
                    var missingProviderKey = $"{item.Id}:{item.ConfigContract}";

                    // If the type is missing
                    if (impType == null)
                    {
                        if (ReportedUnknownProviders.TryAdd(missingProviderKey, 0))
                        {
                            _logger.Warn("Skipping unavailable provider '{0}' (ID {1}, settings type {2}). Its saved configuration is retained. Restore the provider/plugin or remove the obsolete entry. Repeated warnings for this entry are suppressed until the provider returns or Lidarr restarts.", item.Name, item.Id, item.ConfigContract);
                        }

                        continue;
                    }

                    ReportedUnknownProviders.TryRemove(missingProviderKey, out _);

                    if (body.IsNullOrWhiteSpace() || impType == null)
                    {
                        item.Settings = NullConfig.Instance;
                    }
                    else
                    {
                        item.Settings = (IProviderConfig)JsonSerializer.Deserialize(body, impType, _serializerSettings);
                    }

                    results.Add(item);
                }
            }

            return results;
        }
    }
}
