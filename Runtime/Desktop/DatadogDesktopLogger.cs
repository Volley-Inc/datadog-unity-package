// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Datadog.Unity.Logs;
using Newtonsoft.Json;
using UnityEngine;

namespace Datadog.Unity.Desktop
{
    internal class DatadogDesktopLogger : DdLogger
    {
        private readonly DatadogDesktopPlatform _platform;
        private readonly DatadogLoggingOptions _options;
        private readonly Dictionary<string, object> _attributes = new();
        private readonly List<string> _tags = new();

        public DatadogDesktopLogger(
            DdLogLevel logLevel,
            float sampleRate,
            DatadogDesktopPlatform platform,
            DatadogLoggingOptions options)
            : base(logLevel, sampleRate)
        {
            _platform = platform;
            _options = options;
        }

        public override void AddAttribute(string key, object value)
        {
            _attributes[key] = value;
        }

        public override void AddTag(string tag, string value = null)
        {
            var fullTag = value != null ? $"{tag}:{value}" : tag;
            if (!_tags.Contains(fullTag))
            {
                _tags.Add(fullTag);
            }
        }

        public override void RemoveAttribute(string key)
        {
            _attributes.Remove(key);
        }

        public override void RemoveTag(string tag)
        {
            _tags.Remove(tag);
        }

        public override void RemoveTagsWithKey(string key)
        {
            _tags.RemoveAll(t => t == key || t.StartsWith(key + ":"));
        }

        internal override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, ErrorInfo error = null)
        {
            // NOTE: Unlike iOS/Android native SDKs which queue data locally when consent is Pending,
            // this implementation drops data unless consent is Granted.
            if (_platform.TrackingConsent != TrackingConsent.Granted)
            {
                return;
            }

            var options = _platform.Options;
            if (options == null)
            {
                return;
            }

            var logEntry = new Dictionary<string, object>
            {
                { "message", message },
                { "status", DatadogDesktopHelpers.DdLogLevelToStatus(level) },
                { "service", options.ServiceName ?? "unity" },
                { "ddsource", "unity" },
                { "hostname", Environment.MachineName },
            };

            var env = string.IsNullOrEmpty(options.Env) ? "prod" : options.Env;
            logEntry["ddtags"] = BuildDdTags(env);

            if (_options?.Name != null)
            {
                logEntry["logger.name"] = _options.Name;
            }

            // Merge global attributes, then per-logger attributes, then per-call attributes
            foreach (var kvp in _platform.SnapshotGlobalLogAttributes())
            {
                logEntry[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in _attributes)
            {
                logEntry[kvp.Key] = kvp.Value;
            }

            if (attributes != null)
            {
                foreach (var kvp in attributes)
                {
                    logEntry[kvp.Key] = kvp.Value;
                }
            }

            // Add user info if set
            var user = _platform.SnapshotUserInfo();
            if (user.Id != null)
            {
                logEntry["usr.id"] = user.Id;
            }

            if (user.Name != null)
            {
                logEntry["usr.name"] = user.Name;
            }

            if (user.Email != null)
            {
                logEntry["usr.email"] = user.Email;
            }

            foreach (var kvp in user.ExtraInfo)
            {
                logEntry[$"usr.{kvp.Key}"] = kvp.Value;
            }

            // Add error info if present
            if (error != null)
            {
                logEntry["error.kind"] = error.Type;
                logEntry["error.message"] = error.Message;
                if (error.StackTrace != null)
                {
                    logEntry["error.stack"] = error.StackTrace;
                }
            }

            SendLog(logEntry, options);
        }

        private string BuildDdTags(string env)
        {
            var tags = new List<string>
            {
                $"env:{env}",
                $"version:{Application.version}",
                $"sdk_version:{DatadogSdk.SdkVersion}",
            };

            tags.AddRange(_tags);

            return string.Join(",", tags);
        }

        private void SendLog(Dictionary<string, object> logEntry, DatadogConfigurationOptions options)
        {
            var endpoint = string.IsNullOrEmpty(options.CustomEndpoint)
                ? DatadogDesktopHelpers.GetLogsEndpoint(options.Site)
                : options.CustomEndpoint;

            var url = $"{endpoint}/api/v2/logs";
            var jsonPayload = JsonConvert.SerializeObject(new[] { logEntry });

            try
            {
                // Using HttpClient instead of UnityWebRequest because PlatformLog is called
                // from the worker thread, and UnityWebRequest requires the main thread.
                // The DD-API-KEY header is already set on the shared HttpClient instance.
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                using var response = _platform.HttpClient.PostAsync(url, content).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    UnityEngine.Debug.LogWarning($"[Datadog] Failed to send log: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Datadog] Failed to send log: {e.Message}");
            }
        }
    }
}
