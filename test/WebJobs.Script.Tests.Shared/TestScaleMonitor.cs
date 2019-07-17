// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestScaleMonitor<TMetrics> : IScaleMonitor<TMetrics> where TMetrics : ScaleMetrics
    {
        public TestScaleMonitor()
        {
            Index = 0;
        }

        public string Id => GetType().Name.ToLower();

        public Exception Exception { get; set; }

        public int Index { get; set; }

        public List<TMetrics> Metrics { get; set; }

        public ScaleStatus Status { get; set; }

        Task<ScaleMetrics> IScaleMonitor.GetMetricsAsync()
        {
            if (Exception != null)
            {
                throw Exception;
            }

            return Task.FromResult<ScaleMetrics>(Metrics[Index++ % Metrics.Count]);
        }

        public async Task<TMetrics> GetMetricsAsync()
        {
            return (TMetrics)(await GetMetricsAsync());
        }

        ScaleStatus IScaleMonitor.GetScaleStatus(ScaleStatusContext context)
        {
            return Status;
        }

        public ScaleStatus GetScaleStatus(ScaleStatusContext<TMetrics> context)
        {
            return ((IScaleMonitor)this).GetScaleStatus(context);
        }
    }

    public class TestScaleMonitor1 : TestScaleMonitor<TestScaleMetrics1>
    {
    }

    public class TestScaleMetrics1 : ScaleMetrics
    {
        public int Count { get; set; }
    }

    public class TestScaleMonitor2 : TestScaleMonitor<TestScaleMetrics2>
    {
    }

    public class TestScaleMetrics2 : ScaleMetrics
    {
        public int Num { get; set; }
    }

    public class TestScaleMonitor3 : TestScaleMonitor<TestSCaleMetrics3>
    {
    }

    public class TestSCaleMetrics3 : ScaleMetrics
    {
        public int Length { get; set; }
    }
}
