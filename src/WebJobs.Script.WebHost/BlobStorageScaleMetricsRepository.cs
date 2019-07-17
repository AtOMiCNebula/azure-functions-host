// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class BlobStorageScaleMetricsRepository : IScaleMetricsRepository
    {
        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly ScaleOptions _scaleOptions;
        private readonly ILogger _logger;
        private CloudBlockBlob _metricsBlob;

        public BlobStorageScaleMetricsRepository(IConfiguration configuration, IHostIdProvider hostIdProvider, IOptions<ScaleOptions> scaleOptions, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _scaleOptions = scaleOptions.Value;
            _logger = loggerFactory.CreateLogger(ScriptConstants.TraceSourceScale);
        }

        internal CloudBlockBlob MetricsBlob
        {
            get
            {
                if (_metricsBlob == null)
                {
                    string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                    CloudStorageAccount account = null;
                    if (!string.IsNullOrEmpty(storageConnectionString) &&
                        CloudStorageAccount.TryParse(storageConnectionString, out account))
                    {
                        string hostId = _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                        CloudBlobClient blobClient = account.CreateCloudBlobClient();
                        var blobContainer = blobClient.GetContainerReference(ScriptConstants.AzureWebJobsHostsContainerName);
                        string blobPath = $"scale/{hostId}/metrics.json";
                        _metricsBlob = blobContainer.GetBlockBlobReference(blobPath);
                    }
                    else
                    {
                        _logger.LogError("Azure Storage connection string is empty or invalid. Unable to read/write scale metrics.");
                    }
                }

                return _metricsBlob;
            }
        }

        public async Task<IDictionary<IScaleMonitor, IList<ScaleMetrics>>> ReadAsync(IEnumerable<IScaleMonitor> monitors)
        {
            var result = new Dictionary<IScaleMonitor, IList<ScaleMetrics>>();

            if (StorageConnectionValid() && monitors.Any())
            {
                JObject allMetrics = await ReadCurrentMetricsOrDefaultAsync();

                foreach (var monitor in monitors)
                {
                    List<ScaleMetrics> monitorMetrics = new List<ScaleMetrics>();
                    JArray rawMonitorMetrics = (JArray)allMetrics[monitor.Id];

                    var monitorInterfaceType = monitor.GetType().GetInterfaces().SingleOrDefault(p => p.IsGenericType && p.GetGenericTypeDefinition() == typeof(IScaleMonitor<>));
                    if (monitorInterfaceType == null)
                    {
                        // we require the monitor to implement the generic interface in order to know
                        // what type to deserialize into
                        continue;
                    }
                    Type metricsType = monitorInterfaceType.GetGenericArguments()[0];

                    foreach (JObject currMetrics in rawMonitorMetrics)
                    {
                        var deserializedMetrics = (ScaleMetrics)currMetrics.ToObject(metricsType);
                        if ((DateTime.UtcNow - deserializedMetrics.TimeStamp) < _scaleOptions.ScaleMetricsMaxAge)
                        {
                            monitorMetrics.Add(deserializedMetrics);
                        }
                    }

                    result[monitor] = monitorMetrics;
                }
            }

            return result;
        }

        public async Task WriteAsync(IDictionary<IScaleMonitor, ScaleMetrics> monitorMetrics)
        {
            if (!StorageConnectionValid() || monitorMetrics.Count == 0)
            {
                return;
            }

            JObject allMetrics = await ReadCurrentMetricsOrDefaultAsync();

            // When writing the metrics blob back after adding the current metrics, we'll be filtering out any metrics that have expired,
            // or for functions that no longer exist, are disabled.
            JObject newMetrics = new JObject();
            foreach (var pair in monitorMetrics)
            {
                // get the current metrics for this provider if present
                JArray currMetrics = (JArray)allMetrics[pair.Key.Id];
                if (currMetrics == null)
                {
                    currMetrics = new JArray();
                }

                // Purge any out of date metrics
                JArray filteredMetrics = new JArray();
                foreach (var currMetric in currMetrics)
                {
                    var timestamp = (DateTime)currMetric[nameof(ScaleMetrics.TimeStamp)];
                    if ((DateTime.UtcNow - timestamp) < _scaleOptions.ScaleMetricsMaxAge)
                    {
                        filteredMetrics.Add(currMetric);
                    }
                }
                currMetrics = filteredMetrics;

                if (currMetrics.Count == _scaleOptions.ScaleMetricsRetentionCount)
                {
                    // if we're at the max size remove the oldest before appending
                    currMetrics.RemoveAt(0);
                }

                // append the metrics for the current monitor
                currMetrics.Add(JObject.FromObject(pair.Value));

                // add the updated metrics
                newMetrics[pair.Key.Id] = currMetrics;
            }

            // persist the metrics
            string json = newMetrics.ToString();
            await MetricsBlob.UploadTextAsync(json);
        }

        private async Task<JObject> ReadCurrentMetricsOrDefaultAsync()
        {
            // load the existing metrics if present
            JObject metrics = null;
            string content = null;
            if (await MetricsBlob.ExistsAsync())
            {
                content = await MetricsBlob.DownloadTextAsync();
                metrics = JObject.Parse(content);
            }
            else
            {
                metrics = new JObject();
            }

            return metrics;
        }

        /// <summary>
        /// Before reading/writing metrics, we check to see if the app is configured with
        /// a valid storage connection string. If not an error is logged.
        /// </summary>
        /// <returns>True if we have a valid storage connection string, false otherwise.</returns>
        private bool StorageConnectionValid()
        {
            return MetricsBlob != null;
        }
    }
}
