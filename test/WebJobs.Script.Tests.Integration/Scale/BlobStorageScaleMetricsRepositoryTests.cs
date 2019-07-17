// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Scale
{
    public class BlobStorageScaleMetricsRepositoryTests
    {
        private readonly BlobStorageScaleMetricsRepository _repository;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly Mock<IHostIdProvider> _hostIdProviderMock;
        private readonly ScaleOptions _scaleOptions;

        public BlobStorageScaleMetricsRepositoryTests()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            _hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            _hostIdProviderMock.Setup(p => p.GetHostIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("testhostid");
            _scaleOptions = new ScaleOptions();
            _loggerProvider = new TestLoggerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _repository = new BlobStorageScaleMetricsRepository(configuration, _hostIdProviderMock.Object, new OptionsWrapper<ScaleOptions>(_scaleOptions), loggerFactory);
        }

        [Fact]
        public async Task InvalidStorageConnection_Handled()
        {
            var configuration = new ConfigurationBuilder().Build();
            Assert.Null(configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage));

            var options = new ScaleOptions();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            var localRepository = new BlobStorageScaleMetricsRepository(configuration, _hostIdProviderMock.Object, new OptionsWrapper<ScaleOptions>(options), loggerFactory);

            var monitor1 = new TestScaleMonitor1();
            var monitor2 = new TestScaleMonitor2();
            var monitor3 = new TestScaleMonitor3();
            var monitors = new IScaleMonitor[] { monitor1, monitor2, monitor3 };
            var result = await localRepository.ReadAsync(monitors);
            Assert.Empty(result);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Single(logs);
            Assert.Equal("Azure Storage connection string is empty or invalid. Unable to read/write scale metrics.", logs[0].FormattedMessage);

            _loggerProvider.ClearAllLogMessages();
            Dictionary<IScaleMonitor, ScaleMetrics> metricsMap = new Dictionary<IScaleMonitor, ScaleMetrics>();
            metricsMap.Add(monitor1, new TestScaleMetrics1 { Count = 10, TimeStamp = DateTime.UtcNow });
            metricsMap.Add(monitor2, new TestScaleMetrics2 { Num = 50, TimeStamp = DateTime.UtcNow });
            metricsMap.Add(monitor3, new TestSCaleMetrics3 { Length = 100, TimeStamp = DateTime.UtcNow });
            await localRepository.WriteAsync(metricsMap);

            logs = _loggerProvider.GetAllLogMessages();
            Assert.Single(logs);
            Assert.Equal("Azure Storage connection string is empty or invalid. Unable to read/write scale metrics.", logs[0].FormattedMessage);
        }

        [Fact]
        public async Task WriteAsync_PersistsMetricsToBlob()
        {
            await _repository.MetricsBlob.DeleteIfExistsAsync();

            var monitor1 = new TestScaleMonitor1();
            var monitor2 = new TestScaleMonitor2();
            var monitor3 = new TestScaleMonitor3();
            var monitors = new IScaleMonitor[] { monitor1, monitor2, monitor3 };

            // simulate 10 sample iterations
            for (int i = 0; i < 10; i++)
            {
                Dictionary<IScaleMonitor, ScaleMetrics> metricsMap = new Dictionary<IScaleMonitor, ScaleMetrics>();

                metricsMap.Add(monitor1, new TestScaleMetrics1 { Count = i, TimeStamp = DateTime.UtcNow });
                metricsMap.Add(monitor2, new TestScaleMetrics2 { Num = i, TimeStamp = DateTime.UtcNow });
                metricsMap.Add(monitor3, new TestSCaleMetrics3 { Length = i, TimeStamp = DateTime.UtcNow });

                await _repository.WriteAsync(metricsMap);
            }

            // read the json manually and verify
            var json = await _repository.MetricsBlob.DownloadTextAsync();
            var metrics = JObject.Parse(json);
            Assert.Equal(3, metrics.Count);

            var monitorMetricsArray = (JArray)metrics["testscalemonitor1"];
            Assert.Equal(5, monitorMetricsArray.Count);
            for (int i = 0; i < 5; i++)
            {
                var currSample = monitorMetricsArray[i].ToObject<TestScaleMetrics1>();
                Assert.Equal(5 + i, currSample.Count);
                Assert.NotEqual(default(DateTime), currSample.TimeStamp);
            }

            monitorMetricsArray = (JArray)metrics["testscalemonitor2"];
            Assert.Equal(5, monitorMetricsArray.Count);
            for (int i = 0; i < 5; i++)
            {
                var currSample = monitorMetricsArray[i].ToObject<TestScaleMetrics2>();
                Assert.Equal(5 + i, currSample.Num);
                Assert.NotEqual(default(DateTime), currSample.TimeStamp);
            }

            monitorMetricsArray = (JArray)metrics["testscalemonitor3"];
            Assert.Equal(5, monitorMetricsArray.Count);
            for (int i = 0; i < 5; i++)
            {
                var currSample = monitorMetricsArray[i].ToObject<TestSCaleMetrics3>();
                Assert.Equal(5 + i, currSample.Length);
                Assert.NotEqual(default(DateTime), currSample.TimeStamp);
            }

            // read the metrics back via API
            var result = await _repository.ReadAsync(monitors);
            Assert.Equal(3, result.Count);

            var monitorMetricsList = result[monitor1];
            for (int i = 0; i < 5; i++)
            {
                var currSample = (TestScaleMetrics1)monitorMetricsList[i];
                Assert.Equal(5 + i, currSample.Count);
                Assert.NotEqual(default(DateTime), currSample.TimeStamp);
            }

            monitorMetricsList = result[monitor2];
            for (int i = 0; i < 5; i++)
            {
                var currSample = (TestScaleMetrics2)monitorMetricsList[i];
                Assert.Equal(5 + i, currSample.Num);
                Assert.NotEqual(default(DateTime), currSample.TimeStamp);
            }

            monitorMetricsList = result[monitor3];
            for (int i = 0; i < 5; i++)
            {
                var currSample = (TestSCaleMetrics3)monitorMetricsList[i];
                Assert.Equal(5 + i, currSample.Length);
                Assert.NotEqual(default(DateTime), currSample.TimeStamp);
            }

            // if no monitors are presented result will be empty
            monitors = new IScaleMonitor[0];
            result = await _repository.ReadAsync(monitors);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public async Task ReadAsync_FiltersExpiredMetrics()
        {
            var monitor1 = new TestScaleMonitor1();
            var monitors = new IScaleMonitor[] { monitor1 };

            // add a bunch of expired samples
            JArray metricsArray = new JArray();
            for (int i = 5; i > 0; i--)
            {
                var metrics = new TestScaleMetrics1
                {
                    Count = i,
                    TimeStamp = DateTime.UtcNow - _scaleOptions.ScaleMetricsMaxAge - TimeSpan.FromMinutes(i)
                };
                metricsArray.Add(JObject.FromObject(metrics));
            }
            // add a few samples that aren't expired
            for (int i = 3; i > 0; i--)
            {
                var metrics = new TestScaleMetrics1
                {
                    Count = 77,
                    TimeStamp = DateTime.UtcNow - TimeSpan.FromSeconds(i)
                };
                metricsArray.Add(JObject.FromObject(metrics));
            }
            JObject metricsObject = new JObject
            {
                { monitor1.Id, metricsArray }
            };
            await _repository.MetricsBlob.UploadTextAsync(metricsObject.ToString());

            var result = await _repository.ReadAsync(monitors);

            var resultMetrics = result[monitor1].Cast<TestScaleMetrics1>().ToArray();
            Assert.Equal(3, resultMetrics.Length);
            Assert.All(resultMetrics, p => Assert.Equal(77, p.Count));
        }

        [Fact]
        public async Task WriteAsync_FiltersExpiredMetrics()
        {
            var monitor1 = new TestScaleMonitor1();
            var monitors = new IScaleMonitor[] { monitor1 };

            // add a bunch of expired samples
            JArray metricsArray = new JArray();
            for (int i = 5; i > 0; i--)
            {
                var metrics = new TestScaleMetrics1
                {
                    Count = i,
                    TimeStamp = DateTime.UtcNow - _scaleOptions.ScaleMetricsMaxAge - TimeSpan.FromMinutes(i)
                };
                metricsArray.Add(JObject.FromObject(metrics));
            }
            // add a few samples that aren't expired
            for (int i = 3; i > 0; i--)
            {
                var metrics = new TestScaleMetrics1
                {
                    Count = 77,
                    TimeStamp = DateTime.UtcNow - TimeSpan.FromSeconds(i)
                };
                metricsArray.Add(JObject.FromObject(metrics));
            }
            JObject metricsObject = new JObject
            {
                { monitor1.Id, metricsArray }
            };
            await _repository.MetricsBlob.UploadTextAsync(metricsObject.ToString());

            var metricsMap = new Dictionary<IScaleMonitor, ScaleMetrics>();
            metricsMap.Add(monitor1, new TestScaleMetrics1 { Count = 77, TimeStamp = DateTime.UtcNow });

            await _repository.WriteAsync(metricsMap);

            var json = await _repository.MetricsBlob.DownloadTextAsync();
            var parsedObject = JObject.Parse(json);
            var resultMetrics = (JArray)parsedObject[monitor1.Id];

            Assert.Equal(4, resultMetrics.Count);
            Assert.All(resultMetrics, p => Assert.Equal(77, (int)p["Count"]));
        }
    }
}
