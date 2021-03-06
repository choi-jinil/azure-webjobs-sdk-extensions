﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Extensions.CosmosDB.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Extensions.CosmosDB
{
    /// <summary>
    /// Defines the configuration options for the CosmosDB binding.
    /// </summary>
    [Extension("CosmosDB")]
    internal class CosmosDBExtensionConfigProvider : IExtensionConfigProvider
    {
        private readonly IConfiguration _configuration;
        private readonly ICosmosDBServiceFactory _cosmosDBServiceFactory;
        private readonly INameResolver _nameResolver;
        private readonly CosmosDBOptions _options;
        private readonly ILoggerFactory _loggerFactory;

        public CosmosDBExtensionConfigProvider(IOptions<CosmosDBOptions> options, ICosmosDBServiceFactory cosmosDBServiceFactory, IConfiguration configuration, INameResolver nameResolver, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _cosmosDBServiceFactory = cosmosDBServiceFactory;
            _nameResolver = nameResolver;
            _options = options.Value;
            _loggerFactory = loggerFactory;
        }

        internal ConcurrentDictionary<string, ICosmosDBService> ClientCache { get; } = new ConcurrentDictionary<string, ICosmosDBService>();

        /// <inheritdoc />
        public void Initialize(ExtensionConfigContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            // Apply ValidateConnection to all on this rule. 
            var rule = context.AddBindingRule<CosmosDBAttribute>();
            rule.AddValidator(ValidateConnection);
            rule.BindToCollector<DocumentOpenType>(typeof(CosmosDBCollectorBuilder<>), this);

            rule.BindToInput<DocumentClient>(new CosmosDBClientBuilder(this));

            // Enumerable inputs
            rule.WhenIsNull(nameof(CosmosDBAttribute.Id))
                .BindToInput<JArray>(typeof(CosmosDBJArrayBuilder), this);

            rule.WhenIsNull(nameof(CosmosDBAttribute.Id))
                .BindToInput<IEnumerable<DocumentOpenType>>(typeof(CosmosDBEnumerableBuilder<>), this);

            // Single input
            rule.WhenIsNotNull(nameof(CosmosDBAttribute.Id))
                .WhenIsNull(nameof(CosmosDBAttribute.SqlQuery))
                .BindToValueProvider<DocumentOpenType>((attr, t) => BindForItemAsync(attr, t));

            // Trigger
            var rule2 = context.AddBindingRule<CosmosDBTriggerAttribute>();
            rule2.BindToTrigger<IReadOnlyList<Document>>(new CosmosDBTriggerAttributeBindingProvider(_configuration, _nameResolver, _options, this, _loggerFactory));
            rule2.AddConverter<string, IReadOnlyList<Document>>(str => JsonConvert.DeserializeObject<IReadOnlyList<Document>>(str));
            rule2.AddConverter<IReadOnlyList<Document>, JArray>(docList => JArray.FromObject(docList));
            rule2.AddConverter<IReadOnlyList<Document>, string>(docList => JArray.FromObject(docList).ToString());
        }

        internal void ValidateConnection(CosmosDBAttribute attribute, Type paramType)
        {
            if (string.IsNullOrEmpty(_options.ConnectionString) &&
                string.IsNullOrEmpty(attribute.ConnectionStringSetting))
            {
                string attributeProperty = $"{nameof(CosmosDBAttribute)}.{nameof(CosmosDBAttribute.ConnectionStringSetting)}";
                string optionsProperty = $"{nameof(CosmosDBOptions)}.{nameof(CosmosDBOptions.ConnectionString)}";
                throw new InvalidOperationException(
                    $"The CosmosDB connection string must be set either via the '{Constants.DefaultConnectionStringName}' IConfiguration connection string, via the {attributeProperty} property or via {optionsProperty}.");
            }
        }

        internal DocumentClient BindForClient(CosmosDBAttribute attribute)
        {
            string resolvedConnectionString = ResolveConnectionString(attribute.ConnectionStringSetting);
            ICosmosDBService service = GetService(resolvedConnectionString);

            return service.GetClient();
        }

        internal Task<IValueBinder> BindForItemAsync(CosmosDBAttribute attribute, Type type)
        {
            if (string.IsNullOrEmpty(attribute.Id))
            {
                throw new InvalidOperationException("The 'Id' property of a CosmosDB single-item input binding cannot be null or empty.");
            }

            CosmosDBContext context = CreateContext(attribute);

            Type genericType = typeof(CosmosDBItemValueBinder<>).MakeGenericType(type);
            IValueBinder binder = (IValueBinder)Activator.CreateInstance(genericType, context);

            return Task.FromResult(binder);
        }

        internal string ResolveConnectionString(string attributeConnectionString)
        {
            // First, try the Attribute's string.
            if (!string.IsNullOrEmpty(attributeConnectionString))
            {
                return attributeConnectionString;
            }

            // Then use the options.
            return _options.ConnectionString;
        }

        internal ICosmosDBService GetService(string connectionString)
        {
            return ClientCache.GetOrAdd(connectionString, (c) => _cosmosDBServiceFactory.CreateService(c, _options.ConnectionMode, _options.Protocol));
        }

        internal CosmosDBContext CreateContext(CosmosDBAttribute attribute)
        {
            string resolvedConnectionString = ResolveConnectionString(attribute.ConnectionStringSetting);

            ICosmosDBService service = GetService(resolvedConnectionString);

            return new CosmosDBContext
            {
                Service = service,
                ResolvedAttribute = attribute,
            };
        }

        internal static bool IsSupportedEnumerable(Type type)
        {
            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return true;
            }

            return false;
        }

        private class DocumentOpenType : OpenType.Poco
        {
            public override bool IsMatch(Type type, OpenTypeMatchContext context)
            {
                if (type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return false;
                }

                if (type.FullName == "System.Object")
                {
                    return true;
                }

                return base.IsMatch(type, context);
            }
        }
    }
}