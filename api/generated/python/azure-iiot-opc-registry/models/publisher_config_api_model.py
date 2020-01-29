# coding=utf-8
# --------------------------------------------------------------------------
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License. See License.txt in the project root for
# license information.
#
# Code generated by Microsoft (R) AutoRest Code Generator 2.3.33.0
# Changes may cause incorrect behavior and will be lost if the code is
# regenerated.
# --------------------------------------------------------------------------

from msrest.serialization import Model


class PublisherConfigApiModel(Model):
    """Default publisher agent configuration.

    :param capabilities: Capabilities
    :type capabilities: dict[str, str]
    :param job_check_interval: Interval to check job
    :type job_check_interval: str
    :param heartbeat_interval: Heartbeat interval
    :type heartbeat_interval: str
    :param max_workers: Parallel jobs
    :type max_workers: int
    :param job_orchestrator_url: Job orchestrator endpoint url
    :type job_orchestrator_url: str
    """

    _attribute_map = {
        'capabilities': {'key': 'capabilities', 'type': '{str}'},
        'job_check_interval': {'key': 'jobCheckInterval', 'type': 'str'},
        'heartbeat_interval': {'key': 'heartbeatInterval', 'type': 'str'},
        'max_workers': {'key': 'maxWorkers', 'type': 'int'},
        'job_orchestrator_url': {'key': 'jobOrchestratorUrl', 'type': 'str'},
    }

    def __init__(self, capabilities=None, job_check_interval=None, heartbeat_interval=None, max_workers=None, job_orchestrator_url=None):
        super(PublisherConfigApiModel, self).__init__()
        self.capabilities = capabilities
        self.job_check_interval = job_check_interval
        self.heartbeat_interval = heartbeat_interval
        self.max_workers = max_workers
        self.job_orchestrator_url = job_orchestrator_url