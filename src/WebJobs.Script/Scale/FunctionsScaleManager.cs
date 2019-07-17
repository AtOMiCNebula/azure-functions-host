// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Manages scale monitoring operations.
    /// </summary>
    public class FunctionsScaleManager
    {
        private readonly IScaleMonitorManager _monitorManager;
        private readonly IScaleMetricsRepository _metricsRepository;
        private readonly ILogger _logger;

        // for mock testing only
        public FunctionsScaleManager()
        {
        }

        public FunctionsScaleManager(IScaleMonitorManager monitorManager, IScaleMetricsRepository metricsRepository, ILoggerFactory loggerFactory)
        {
            _monitorManager = monitorManager;
            _metricsRepository = metricsRepository;
            _logger = loggerFactory.CreateLogger(ScriptConstants.TraceSourceScale);
        }

        /// <summary>
        /// Get the current scale status (vote) by querying all active monitors for their
        /// scale status.
        /// </summary>
        /// <param name="context">The context to use for the scale decision.</param>
        /// <returns>The scale vote.</returns>
        public virtual async Task<ScaleStatusResult> GetScaleStatusAsync(ScaleStatusContext context)
        {
            var monitors = _monitorManager.GetMonitors();

            List<ScaleVote> votes = new List<ScaleVote>();
            if (monitors.Any())
            {
                // get the collection of current metrics for each monitor
                var monitorMetrics = await _metricsRepository.ReadAsync(monitors);

                _logger.LogInformation($"Computing scale status (WorkerCount={context.WorkerCount})");
                _logger.LogInformation($"{monitorMetrics.Count} scale monitors to sample");

                // for each monitor, ask it to return its scale status (vote) based on
                // the metrics and context info (e.g. worker count)
                foreach (var pair in monitorMetrics)
                {
                    var monitor = pair.Key;
                    var metrics = pair.Value;

                    try
                    {
                        context.Metrics = metrics;
                        var result = monitor.GetScaleStatus(context);

                        _logger.LogInformation($"Monitor '{monitor.Id}' voted '{result.Vote.ToString()}'");
                        votes.Add(result.Vote);
                    }
                    catch (Exception exc) when (!exc.IsFatal())
                    {
                        // if a particular monitor fails, log and continue
                        _logger.LogError(exc, $"Failed to query scale status for monitor '{monitor.Id}'.");
                    }
                }
            }
            else
            {
                // no monitors registered
                // this can happen if the host is offline
            }

            var vote = GetAggregateScaleVote(votes, context, _logger);

            return new ScaleStatusResult
            {
                Vote = vote
            };
        }

        internal static ScaleVote GetAggregateScaleVote(List<ScaleVote> votes, ScaleStatusContext context, ILogger logger)
        {
            ScaleVote vote = ScaleVote.None;
            if (votes.Any())
            {
                // aggregate all the votes into a single vote
                if (votes.Any(p => p == ScaleVote.ScaleOut))
                {
                    // scale out if at least 1 monitor requires it
                    logger.LogInformation("Scaling out based on votes");
                    vote = ScaleVote.ScaleOut;
                }
                else if (context.WorkerCount > 0 && votes.All(p => p == ScaleVote.ScaleIn))
                {
                    // scale in only if all monitors vote scale in
                    logger.LogInformation("Scaling in based on votes");
                    vote = ScaleVote.ScaleIn;
                }
            }
            else if (context.WorkerCount > 0)
            {
                // if no functions exist or are enabled we'll scale in
                logger.LogInformation("No enabled functions or scale votes so scaling in");
                vote = ScaleVote.ScaleIn;
            }

            return vote;
        }
    }
}
