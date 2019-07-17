// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Defines configuration options for runtime scale monitoring.
    /// </summary>
    public class ScaleOptions
    {
        private int _scaleMetricsRetentionCount;

        public ScaleOptions()
        {
            _scaleMetricsRetentionCount = 5;
            ScaleMetricsMaxAge = TimeSpan.FromMinutes(5);
            ScaleMetricsSampleInterval = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Gets or sets a value indicating the maximum number of samples per
        /// scale monitor that should be retained.
        /// </summary>
        public int ScaleMetricsRetentionCount
        {
            get
            {
                return _scaleMetricsRetentionCount;
            }

            set
            {
                if (value < 5 || value > 25)
                {
                    throw new ArgumentOutOfRangeException(nameof(ScaleMetricsRetentionCount), $"{nameof(ScaleMetricsRetentionCount)} must be greater than 5 and less than 25.");
                }
                _scaleMetricsRetentionCount = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the maximum age for metrics.
        /// Metrics that exceed this age will be purged.
        /// </summary>
        public TimeSpan ScaleMetricsMaxAge { get; set; }

        /// <summary>
        /// Gets or sets the sampling interval for scale metrics.
        /// </summary>
        public TimeSpan ScaleMetricsSampleInterval { get; set; }
    }
}
