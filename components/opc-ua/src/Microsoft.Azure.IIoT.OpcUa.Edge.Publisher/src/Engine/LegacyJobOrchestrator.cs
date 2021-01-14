// ------------------------------------------------------------
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

            var file = Path.GetFileName(_legacyCliModel.PublishedNodesFile);
            _fileSystemWatcher = new FileSystemWatcher(directory, file);
            _fileSystemWatcher.Changed += _fileSystemWatcher_Changed;
            _fileSystemWatcher.Created += _fileSystemWatcher_Created;
            _fileSystemWatcher.Renamed += _fileSystemWatcher_Renamed;
            _fileSystemWatcher.EnableRaisingEvents = true;
            RefreshJobFromFileAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets the next available job - this will always return the job representation of the legacy publishednodes.json
        /// along with legacy command line arguments.
        /// </summary>
        /// <param name="workerId"></param>
        /// <param name="request"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<JobProcessingInstructionModel> GetAvailableJobAsync(string workerId, JobRequestModel request, CancellationToken ct = default) {
            try {
                await _lock.WaitAsync();

                if (_availableJobs.Count > 0 && _availableJobs.TryDequeue(out var job)) {
                    _assignedJobs.AddOrUpdate(workerId, job);
                }
                else {
                    _assignedJobs.TryGetValue(workerId, out job);
                }

                return job;
            }
            catch(Exception e) {
                _logger.Error(e, "Failed to get available job for worked {workedId}", workerId);
                return null;
            }
            finally {
                _lock.Release();
            }
        }

        /// <summary>
        /// Receives the heartbeat from the agent. Lifetime information is not persisted in this implementation. This method is
        /// only used if the
        /// publishednodes.json file has changed. Is that the case, the worker is informed to cancel (and restart) processing.
        /// </summary>
        /// <param name="heartbeat"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<HeartbeatResultModel> SendHeartbeatAsync(HeartbeatModel heartbeat, CancellationToken ct = default) {
            try {
                await _lock.WaitAsync();
                _logger.Information("SendHeartbeatAsync for worker {worker}, job {jobId}... ",
                    heartbeat?.Worker?.WorkerId,
                    heartbeat?.Job?.JobId);
                HeartbeatResultModel heartbeatResultModel;
                if (heartbeat.Job != null) {
                    if (_assignedJobs.TryGetValue(heartbeat.Worker.WorkerId, out var job)
                        && job.Job.Id == heartbeat.Job.JobId) {
                        heartbeatResultModel = new HeartbeatResultModel {
                            HeartbeatInstruction = HeartbeatInstruction.Keep,
                            LastActiveHeartbeat = DateTime.UtcNow,
                            UpdatedJob = null,
                        };
                    }
                    else {
                        heartbeatResultModel = new HeartbeatResultModel {
                            HeartbeatInstruction = HeartbeatInstruction.CancelProcessing,
                            LastActiveHeartbeat = DateTime.UtcNow,
                            UpdatedJob = job,
                        };
                    }
                }
                else {
                    heartbeatResultModel = new HeartbeatResultModel {
                        HeartbeatInstruction = HeartbeatInstruction.Keep,
                        LastActiveHeartbeat = DateTime.UtcNow,
                        UpdatedJob = null,
                    };
                }
                _logger.Information("SendHeartbeatAsync updated worker {worker} with {heartbeatInstruction} instruction for job {jobId}.",
                    heartbeat.Worker.WorkerId,
                    heartbeatResultModel?.HeartbeatInstruction,
                    heartbeatResultModel?.UpdatedJob?.Job.Id);
                return heartbeatResultModel;
            }
            finally {
                _lock.Release();
            }
        }

        private void _fileSystemWatcher_Changed(object sender, FileSystemEventArgs e) {
            _logger.Information("File {publishedNodesFile} change trigger ...", _legacyCliModel.PublishedNodesFile);
            RefreshJobFromFileAsync().GetAwaiter().GetResult();
        }

        private void _fileSystemWatcher_Created(object sender, FileSystemEventArgs e) {
            _logger.Information("File {publishedNodesFile} created trigger ...", _legacyCliModel.PublishedNodesFile);
            RefreshJobFromFileAsync().GetAwaiter().GetResult();
        }

        private void _fileSystemWatcher_Renamed(object sender, FileSystemEventArgs e) {
            _logger.Information("File {publishedNodesFile} renamed trigger ...", _legacyCliModel.PublishedNodesFile);
            RefreshJobFromFileAsync().GetAwaiter().GetResult();
        }

        private static string GetChecksum(string content) {
            if (String.IsNullOrEmpty(content)) {
                return null;
            }
            var sha = new SHA256Managed();
            var checksum = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(checksum).Replace("-", string.Empty);
        }

        private async Task RefreshJobFromFileAsync() {
            var retryCount = 0;
            while (true) {
                try {
                    await _lock.WaitAsync();
                    _logger.Information(
                        "File {publishedNodesFile} reload started from thread {threadId} with current lock count: {currentCount}...",
                        _legacyCliModel.PublishedNodesFile,
                        Thread.CurrentThread.ManagedThreadId,
                        _lock.CurrentCount);
                    Task.Delay((int)Math.Pow(500, retryCount+1)).GetAwaiter().GetResult();
                    var availableJobs = new ConcurrentQueue<JobProcessingInstructionModel>();
                    using (var fileStream = new FileStream(_legacyCliModel.PublishedNodesFile, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        var content = fileStream.ReadAsString(Encoding.UTF8);
                        var currentFileHash = GetChecksum(content);
                        _logger.Information(
                            "File {publishedNodesFile} has hash: {hash}, old being hash {oldHash}",
                            _legacyCliModel.PublishedNodesFile, currentFileHash, _lastKnownFileHash);
                        if (currentFileHash != _lastKnownFileHash) {
                            _lastKnownFileHash = currentFileHash;
                            _logger.Information("Processing new content: {content} ... ", content);
                            if (!string.IsNullOrEmpty(content)) {
                                var jobs = _publishedNodesJobConverter.Read(content, _legacyCliModel);
                                foreach (var job in jobs) {

                                    var jobId = string.IsNullOrEmpty(job.WriterGroup.WriterGroupId) 
                                        ? $"Standalone_{_identity.DeviceId}_{Guid.NewGuid()} "
                                        : job.WriterGroup.WriterGroupId;

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
                            if (_agentConfig.MaxWorkers < availableJobs.Count + _availableJobs.Count) {
                                _agentConfig.MaxWorkers = availableJobs.Count + _availableJobs.Count;
                            }

                            _assignedJobs.Clear();
                            foreach (var job in availableJobs) {
                                _availableJobs.Enqueue(job);
                            }
                        }
                    }
                    break;
                }
                catch (IOException ex) {
                    retryCount++;
                    if (retryCount < 4) {
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
                catch (Exception e) {
                    _logger.Error(e, "Unhandled exception.");
                    throw;
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
        private readonly ConcurrentQueue<JobProcessingInstructionModel> _availableJobs;
        private readonly ConcurrentDictionary<string, JobProcessingInstructionModel> _assignedJobs;
        private string _lastKnownFileHash;
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1,1);
    }
}