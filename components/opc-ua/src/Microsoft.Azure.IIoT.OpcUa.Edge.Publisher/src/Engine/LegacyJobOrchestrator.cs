﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.OpcUa.Edge.Publisher.Engine {
    using Microsoft.Azure.IIoT.OpcUa.Edge.Publisher.Models;
    using Microsoft.Azure.IIoT.Agent.Framework;
    using Microsoft.Azure.IIoT.Agent.Framework.Models;
    using Microsoft.Azure.IIoT.Module;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Concurrent;
    using System.Linq;
    using Microsoft.Azure.IIoT.Exceptions;
    using System.Text;

    /// <summary>
    /// Job orchestrator the represents the legacy publishednodes.json with legacy command line arguments as job.
    /// </summary>
    public class LegacyJobOrchestrator : IJobOrchestrator {
        /// <summary>
        /// Creates a new class of the LegacyJobOrchestrator.
        /// </summary>
        /// <param name="publishedNodesJobConverter">The converter to read the job from the specified file.</param>
        /// <param name="legacyCliModelProvider">The provider that provides the legacy command line arguments.</param>
        /// <param name="agentConfigProvider">The provider that provides the agent configuration.</param>
        /// <param name="jobSerializer">The serializer to (de)serialize job information.</param>
        /// <param name="logger">Logger to write log messages.</param>
        /// <param name="identity">Module's identity provider.</param>

        public LegacyJobOrchestrator(PublishedNodesJobConverter publishedNodesJobConverter,
            ILegacyCliModelProvider legacyCliModelProvider, IAgentConfigProvider agentConfigProvider,
            IJobSerializer jobSerializer, ILogger logger, IIdentity identity) {
            _publishedNodesJobConverter = publishedNodesJobConverter
                ?? throw new ArgumentNullException(nameof(publishedNodesJobConverter));
            _legacyCliModel = legacyCliModelProvider.LegacyCliModel
                    ?? throw new ArgumentNullException(nameof(legacyCliModelProvider));
            _agentConfig = agentConfigProvider.Config
                    ?? throw new ArgumentNullException(nameof(agentConfigProvider));

            _jobSerializer = jobSerializer ?? throw new ArgumentNullException(nameof(jobSerializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));

            var directory = Path.GetDirectoryName(_legacyCliModel.PublishedNodesFile);

            if (string.IsNullOrWhiteSpace(directory)) {
                directory = Environment.CurrentDirectory;
            }

            _availableJobs = new ConcurrentQueue<JobProcessingInstructionModel>();
            _assignedJobs = new ConcurrentDictionary<string, JobProcessingInstructionModel>();

            _lock = new SemaphoreSlim(1, 1);

            var file = Path.GetFileName(_legacyCliModel.PublishedNodesFile);
            _fileSystemWatcher = new FileSystemWatcher(directory, file);
            _fileSystemWatcher.Changed += _fileSystemWatcher_Changed;
            _fileSystemWatcher.Created += _fileSystemWatcher_Created;
            _fileSystemWatcher.Renamed += _fileSystemWatcher_Renamed;
            _fileSystemWatcher.EnableRaisingEvents = true;
            RefreshJobFromFile(false);
        }

        /// <summary>
        /// Gets the next available job - this will always return the job representation of the legacy publishednodes.json
        /// along with legacy command line arguments.
        /// </summary>
        /// <param name="workerId"></param>
        /// <param name="request"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Task<JobProcessingInstructionModel> GetAvailableJobAsync(string workerId, JobRequestModel request, CancellationToken ct = default) {
            if (_assignedJobs.TryGetValue(workerId, out var job)) {
                return Task.FromResult(job);
            }
            if (_availableJobs.Count > 0 && _availableJobs.TryDequeue(out job)) {
                _assignedJobs.AddOrUpdate(workerId, job);
                if (_availableJobs.Count == 0) {
                    _updated = false;
                }
            }
            else {
                _updated = false;
            }

            return Task.FromResult(job);
        }

        /// <summary>
        /// Receives the heartbeat from the agent. Lifetime information is not persisted in this implementation. This method is
        /// only used if the
        /// publishednodes.json file has changed. Is that the case, the worker is informed to cancel (and restart) processing.
        /// </summary>
        /// <param name="heartbeat"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Task<HeartbeatResultModel> SendHeartbeatAsync(HeartbeatModel heartbeat, CancellationToken ct = default) {
            HeartbeatResultModel heartbeatResultModel;

            if (heartbeat.Job != null && (_updated || (!_assignedJobs.Any() && !_availableJobs.Any()))) {
                if (_availableJobs.Count == 0) {
                    _updated = false;
                }

                heartbeatResultModel = new HeartbeatResultModel {
                    HeartbeatInstruction = HeartbeatInstruction.CancelProcessing,
                    LastActiveHeartbeat = DateTime.UtcNow,
                    UpdatedJob = _assignedJobs.TryGetValue(heartbeat.Worker.WorkerId, out var job) ? job : null
                };
            }
            else {
                heartbeatResultModel = new HeartbeatResultModel {
                    HeartbeatInstruction = HeartbeatInstruction.Keep,
                    LastActiveHeartbeat = DateTime.UtcNow,
                    UpdatedJob = null
                };
            }

            return Task.FromResult(heartbeatResultModel);
        }

        private void _fileSystemWatcher_Changed(object sender, FileSystemEventArgs e) {
            _logger.Information("File {publishedNodesFile} change trigger ...", _legacyCliModel.PublishedNodesFile);
            RefreshJobFromFile(false);
        }

        private void _fileSystemWatcher_Created(object sender, FileSystemEventArgs e) {
            _logger.Information("File {publishedNodesFile} created trigger ...", _legacyCliModel.PublishedNodesFile);
            RefreshJobFromFile(false);
        }

        private void _fileSystemWatcher_Renamed(object sender, FileSystemEventArgs e) {
            _logger.Information("File {publishedNodesFile} Renamed trigger ...", _legacyCliModel.PublishedNodesFile);
            RefreshJobFromFile(true);
        }

        private static string GetChecksum(string content) {
            if (String.IsNullOrEmpty(content)) {
                return null;
            }
            var sha = new SHA256Managed();
            var checksum = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(checksum).Replace("-", string.Empty);
        }

        private void RefreshJobFromFile(bool renamed) {
            var retryCount = 3;
            while (true) {
                try {
                    _lock.Wait();
                    //  Task.Delay(1000).GetAwaiter().GetResult();
                    var availableJobs = new ConcurrentQueue<JobProcessingInstructionModel>();
                    using (var reader = new StreamReader(_legacyCliModel.PublishedNodesFile)) {
                        var content = reader.ReadToEnd();
                        var currentFileHash = GetChecksum(content);
                        if (currentFileHash != _lastKnownFileHash) {
                            _logger.Information("File {publishedNodesFile} with new hash: {hash} has changed, old hash {oldHash}; current lock count: {currentCount} reloading...",
                                _legacyCliModel.PublishedNodesFile,
                                currentFileHash,
                                _lastKnownFileHash,
                                _lock.CurrentCount);
                            _lastKnownFileHash = currentFileHash;
                            _logger.Information("Content: {content}", content);
                            if (!String.IsNullOrEmpty(content)) {
                                var jobs = _publishedNodesJobConverter.Read(content, _legacyCliModel);
                                foreach (var job in jobs) {
                                    var jobId = $"Standalone_{_identity.DeviceId}_{_identity.ModuleId}";
                                    job.WriterGroup.DataSetWriters.ForEach(d => {
                                        d.DataSet.ExtensionFields ??= new Dictionary<string, string>();
                                        d.DataSet.ExtensionFields["PublisherId"] = jobId;
                                        d.DataSet.ExtensionFields["DataSetWriterId"] = d.DataSetWriterId;
                                    });
                                    var endpoints = string.Join(", ", job.WriterGroup.DataSetWriters.Select(w => w.DataSet.DataSetSource.Connection.Endpoint.Url));
                                    _logger.Information($"Job {jobId} loaded. DataSetWriters endpoints: {endpoints}");
                                    var serializedJob = _jobSerializer.SerializeJobConfiguration(job, out var jobConfigurationType);

                                    availableJobs.Enqueue(
                                        new JobProcessingInstructionModel {
                                            Job = new JobInfoModel {
                                                Demands = new List<DemandModel>(),
                                                Id = jobId,
                                                JobConfiguration = serializedJob,
                                                JobConfigurationType = jobConfigurationType,
                                                LifetimeData = new JobLifetimeDataModel(),
                                                Name = jobId,
                                                RedundancyConfig = new RedundancyConfigModel { DesiredActiveAgents = 1, DesiredPassiveAgents = 0 }
                                            },
                                            ProcessMode = ProcessMode.Active
                                        });
                                }
                            }
                            _agentConfig.MaxWorkers = availableJobs.Count;
                            ThreadPool.GetMinThreads(out var workerThreads, out var asyncThreads);
                            if (_agentConfig.MaxWorkers > workerThreads ||
                                _agentConfig.MaxWorkers > asyncThreads) {
                                var result = ThreadPool.SetMinThreads(_agentConfig.MaxWorkers.Value, _agentConfig.MaxWorkers.Value);
                                _logger.Information("Thread pool changed to: worker {worker}, async {async} threads {succeeded}",
                                    _agentConfig.MaxWorkers.Value, _agentConfig.MaxWorkers.Value, result ? "succeeded" : "failed");
                            }
                            _availableJobs = availableJobs;
                            _assignedJobs.Clear();
                            _updated = true;
                        }
                    }
                    break;
                }
                catch (IOException ex) {
                    retryCount--;
                    if (retryCount > 0) {
                        _logger.Debug("Error while loading job from file, retrying...");
                    }
                    else {
                        _logger.Error(ex, "Error while loading job from file. Retry expired, giving up.");
                        break;
                    }
                }
                catch (SerializerException sx) {
                    _logger.Error(sx, "SerializerException while loading job from file.");
                    break;
                }
                finally {
                    _logger.Information("File {publishedNodesFile} has changed, reloading finalized", _legacyCliModel.PublishedNodesFile);
                    _lock.Release();
                }
            }
        }

        private readonly FileSystemWatcher _fileSystemWatcher;
        private readonly IJobSerializer _jobSerializer;
        private readonly LegacyCliModel _legacyCliModel;
        private readonly AgentConfigModel _agentConfig;
        private readonly IIdentity _identity;
        private readonly ILogger _logger;

        private readonly PublishedNodesJobConverter _publishedNodesJobConverter;
        private ConcurrentQueue<JobProcessingInstructionModel> _availableJobs;
        private readonly ConcurrentDictionary<string, JobProcessingInstructionModel> _assignedJobs;
        private string _lastKnownFileHash;
        private bool _updated;
        private readonly SemaphoreSlim _lock;
    }
}