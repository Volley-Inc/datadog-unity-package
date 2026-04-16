// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.Desktop
{
    [Preserve]
    public static class DatadogInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void InitializeDatadog()
        {
            var options = DatadogConfigurationOptions.Load();
            if (options != null && options.Enabled)
            {
                var datadogPlatform = new DatadogDesktopPlatform();
                datadogPlatform.Init(options);
                DatadogSdk.InitWithPlatform(datadogPlatform, options);
            }
        }
    }

    internal class DatadogDesktopPlatform : IDatadogPlatform
    {
        private DatadogConfigurationOptions _options;
        private CoreLoggerLevel _verbosity = CoreLoggerLevel.Warn;
        private TrackingConsent _trackingConsent = TrackingConsent.Pending;
        private HttpClient _httpClient;

        private string _userId;
        private string _userName;
        private string _userEmail;
        private Dictionary<string, object> _userExtraInfo = new();
        private Dictionary<string, object> _globalLogAttributes = new();

        public DatadogWorker CreateWorker(IInternalLogger logger)
        {
            return new ThreadedWorker(logger);
        }

        public void Init(DatadogConfigurationOptions options)
        {
            _options = options;
            SetVerbosity(options.SdkVerbosity);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("DD-API-KEY", options.ClientToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var environment = string.IsNullOrEmpty(options.Env) ? "prod" : options.Env;
            Debug.Log($"[Datadog] Desktop platform initialized. Service: {options.ServiceName}, Env: {environment}, Site: {options.Site}");
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
            _verbosity = logLevel;
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            _trackingConsent = trackingConsent;
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
            _userId = id;
            _userName = name;
            _userEmail = email;
            _userExtraInfo = extraInfo ?? new Dictionary<string, object>();
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (extraInfo == null)
            {
                return;
            }

            _userExtraInfo.Copy(extraInfo);
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            var innerLogger = new DatadogDesktopLogger(
                options.RemoteLogThreshold,
                options.RemoteSampleRate,
                this,
                options);
            return new DdWorkerProxyLogger(worker, innerLogger);
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            if (attributes == null)
            {
                return;
            }

            _globalLogAttributes.Copy(attributes);
        }

        public void RemoveLogsAttribute(string key)
        {
            if (key == null)
            {
                return;
            }

            _globalLogAttributes.Remove(key);
        }

        public IDdRumInternal InitRum(DatadogConfigurationOptions options)
        {
            return new DatadogDesktopRum(this, options);
        }

        public void SendDebugTelemetry(string message)
        {
            if (_verbosity <= CoreLoggerLevel.Debug)
            {
                Debug.Log($"[Datadog Telemetry] {message}");
            }
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
            Debug.LogWarning($"[Datadog Telemetry Error] {kind}: {message}\n{stack}");
        }

        public void ClearAllData()
        {
            _globalLogAttributes.Clear();
            _userExtraInfo.Clear();
            _userId = null;
            _userName = null;
            _userEmail = null;
        }

        public string GetNativeStack(Exception error)
        {
            return string.Empty;
        }

        internal HttpClient HttpClient => _httpClient;

        internal DatadogConfigurationOptions Options => _options;

        internal TrackingConsent TrackingConsent => _trackingConsent;

        internal string UserId => _userId;

        internal string UserName => _userName;

        internal string UserEmail => _userEmail;

        internal Dictionary<string, object> UserExtraInfo => _userExtraInfo;

        internal Dictionary<string, object> GlobalLogAttributes => _globalLogAttributes;
    }
}
