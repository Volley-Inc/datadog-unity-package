// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Datadog.Unity.Rum;
using Newtonsoft.Json;
using UnityEngine;

namespace Datadog.Unity.Desktop
{
    internal class DatadogDesktopRum : IDdRumInternal
    {
        private readonly DatadogDesktopPlatform _platform;
        private readonly DatadogConfigurationOptions _options;
        private readonly Dictionary<string, object> _globalAttributes = new();
        private readonly Dictionary<string, PendingResource> _pendingResources = new();
        private readonly Dictionary<string, object> _featureFlags = new();

        // Guards _globalAttributes and view/session fields. All RUM API methods run on the worker
        // thread except AddLongTask, which is invoked from the main thread and dispatches to the
        // threadpool — without the lock that races with worker-thread mutations and iteration.
        private readonly object _stateLock = new();

        private class PendingResource
        {
            public RumHttpMethod Method;
            public string Url;
            public DateTimeOffset StartTime;
            public Dictionary<string, object> Attributes;
        }

        private string _sessionId;
        private string _viewId;
        private string _viewKey;
        private string _viewName;
        private DateTimeOffset _viewStartTime;
        private int _viewDocumentVersion;
        private int _viewActionCount;
        private int _viewErrorCount;
        private int _viewResourceCount;
        private double _refreshRateSum;
        private double _refreshRateMin;
        private int _refreshRateSampleCount;

        public DatadogDesktopRum(
            DatadogDesktopPlatform platform,
            DatadogConfigurationOptions options
        )
        {
            _platform = platform;
            _options = options;
            _sessionId = Guid.NewGuid().ToString();
        }

        public void StartView(
            string key,
            string name = null,
            Dictionary<string, object> attributes = null
        )
        {
            lock (_stateLock)
            {
                _viewId = Guid.NewGuid().ToString();
                _viewKey = key;
                _viewName = name ?? key;
                _viewStartTime = DateTimeOffset.UtcNow;
                _viewDocumentVersion = 1;
                _viewActionCount = 0;
                _viewErrorCount = 0;
                _viewResourceCount = 0;
                _refreshRateSum = 0;
                _refreshRateMin = double.MaxValue;
                _refreshRateSampleCount = 0;
                _featureFlags.Clear();
            }

            SendViewEvent(attributes);
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            lock (_stateLock)
            {
                if (_viewId == null)
                {
                    return;
                }

                _viewDocumentVersion++;
            }

            SendViewEvent(attributes);
        }

        private void SendViewEvent(
            Dictionary<string, object> attributes,
            Dictionary<string, object> featureFlags = null
        )
        {
            var timeSpentNs =
                (long)((DateTimeOffset.UtcNow - _viewStartTime).TotalMilliseconds * 1_000_000.0);
            if (timeSpentNs < 0)
            {
                timeSpentNs = 0;
            }

            var viewData = new Dictionary<string, object>
            {
                { "id", _viewId },
                { "url", _viewKey },
                { "name", _viewName },
                { "time_spent", timeSpentNs },
                {
                    "action",
                    new Dictionary<string, object> { { "count", _viewActionCount } }
                },
                {
                    "error",
                    new Dictionary<string, object> { { "count", _viewErrorCount } }
                },
                {
                    "resource",
                    new Dictionary<string, object> { { "count", _viewResourceCount } }
                },
            };

            if (_refreshRateSampleCount > 0)
            {
                viewData["refresh_rate_average"] = _refreshRateSum / _refreshRateSampleCount;
                viewData["refresh_rate_min"] = _refreshRateMin;
            }

            var rumEvent = CreateBaseEvent("view");
            rumEvent["view"] = viewData;

            // Override _dd with document_version required by view events
            rumEvent["_dd"] = new Dictionary<string, object>
            {
                { "format_version", 2 },
                { "document_version", _viewDocumentVersion },
            };

            // Caller may pass an explicit snapshot (e.g. AddFeatureFlagEvaluation captured it
            // atomically with the document_version bump); otherwise read the current set.
            if (featureFlags == null)
            {
                lock (_stateLock)
                {
                    if (_featureFlags.Count > 0)
                    {
                        featureFlags = new Dictionary<string, object>(_featureFlags);
                    }
                }
            }

            if (featureFlags != null && featureFlags.Count > 0)
            {
                rumEvent["feature_flags"] = featureFlags;
            }

            MergeAttributes(rumEvent, attributes);
            SendRumEvent(rumEvent);
        }

        public void AddAction(
            RumUserActionType type,
            string name,
            Dictionary<string, object> attributes = null
        )
        {
            if (_viewId == null)
            {
                return;
            }

            _viewActionCount++;

            var rumEvent = CreateBaseEvent("action");
            rumEvent["action"] = new Dictionary<string, object>
            {
                { "id", Guid.NewGuid().ToString() },
                { "type", DatadogDesktopHelpers.RumActionTypeToString(type) },
                {
                    "target",
                    new Dictionary<string, object> { { "name", name } }
                },
            };

            InjectViewContext(rumEvent);
            MergeAttributes(rumEvent, attributes);
            SendRumEvent(rumEvent);
        }

        public void StartAction(
            RumUserActionType type,
            string name,
            Dictionary<string, object> attributes = null
        )
        {
            // For simplicity, treat start/stop actions the same as discrete actions on desktop.
            // The native SDKs track duration internally; we emit the action on start.
            AddAction(type, name, attributes);
        }

        public void StopAction(
            RumUserActionType type,
            string name,
            Dictionary<string, object> attributes = null
        )
        {
            // No-op — action was already emitted in StartAction.
        }

        public void AddError(
            ErrorInfo error,
            RumErrorSource source,
            Dictionary<string, object> attributes = null
        )
        {
            if (error == null || _viewId == null)
            {
                return;
            }

            _viewErrorCount++;

            var errorData = new Dictionary<string, object>
            {
                { "message", error.Message },
                { "source", DatadogDesktopHelpers.RumErrorSourceToString(source) },
            };

            if (error.Type != null)
            {
                errorData["type"] = error.Type;
            }

            if (error.StackTrace != null)
            {
                errorData["stack"] = error.StackTrace;
            }

            var rumEvent = CreateBaseEvent("error");
            rumEvent["error"] = errorData;

            InjectViewContext(rumEvent);
            MergeAttributes(rumEvent, attributes);
            SendRumEvent(rumEvent);
        }

        public void StartResource(
            string key,
            RumHttpMethod httpMethod,
            string url,
            Dictionary<string, object> attributes = null
        )
        {
            _pendingResources[key] = new PendingResource
            {
                Method = httpMethod,
                Url = url,
                StartTime = DateTimeOffset.UtcNow,
                Attributes = attributes,
            };
        }

        public void StopResource(
            string key,
            RumResourceType kind,
            int? statusCode = null,
            long? size = null,
            Dictionary<string, object> attributes = null
        )
        {
            if (_viewId == null || !_pendingResources.TryGetValue(key, out var pending))
            {
                return;
            }

            _pendingResources.Remove(key);
            _viewResourceCount++;

            var durationNs =
                (long)((DateTimeOffset.UtcNow - pending.StartTime).TotalMilliseconds * 1_000_000.0);
            if (durationNs < 0)
            {
                durationNs = 0;
            }

            var resourceData = new Dictionary<string, object>
            {
                { "id", Guid.NewGuid().ToString() },
                { "type", DatadogDesktopHelpers.RumResourceTypeToString(kind) },
                { "url", pending.Url },
                { "method", DatadogDesktopHelpers.RumHttpMethodToString(pending.Method) },
                { "duration", durationNs },
            };

            if (statusCode.HasValue)
            {
                resourceData["status_code"] = statusCode.Value;
            }

            if (size.HasValue)
            {
                resourceData["size"] = size.Value;
            }

            var rumEvent = CreateBaseEvent("resource");
            rumEvent["resource"] = resourceData;

            InjectViewContext(rumEvent);
            MergeAttributes(rumEvent, MergeDicts(pending.Attributes, attributes));
            SendRumEvent(rumEvent);
        }

        public void StopResourceWithError(
            string key,
            string errorType,
            string errorMessage,
            Dictionary<string, object> attributes = null
        )
        {
            StopResourceWithError(key, new ErrorInfo(errorType, errorMessage), attributes);
        }

        [Obsolete]
        public void StopResource(
            string key,
            Exception error,
            Dictionary<string, object> attributes = null
        )
        {
            StopResourceWithError(key, new ErrorInfo(error), attributes);
        }

        public void StopResourceWithError(
            string key,
            ErrorInfo error,
            Dictionary<string, object> attributes = null
        )
        {
            if (error == null || _viewId == null)
            {
                return;
            }

            _pendingResources.Remove(key);
            _viewErrorCount++;

            var errorData = new Dictionary<string, object>
            {
                { "message", error.Message },
                { "source", "network" },
            };

            if (error.Type != null)
            {
                errorData["type"] = error.Type;
            }

            if (error.StackTrace != null)
            {
                errorData["stack"] = error.StackTrace;
            }

            var rumEvent = CreateBaseEvent("error");
            rumEvent["error"] = errorData;

            InjectViewContext(rumEvent);
            MergeAttributes(rumEvent, attributes);
            SendRumEvent(rumEvent);
        }

        private static Dictionary<string, object> MergeDicts(
            Dictionary<string, object> a,
            Dictionary<string, object> b
        )
        {
            if (a == null)
                return b;
            if (b == null)
                return a;
            var merged = new Dictionary<string, object>(a);
            foreach (var kvp in b)
            {
                merged[kvp.Key] = kvp.Value;
            }
            return merged;
        }

        public void AddAttribute(string key, object value)
        {
            if (key == null)
            {
                return;
            }

            lock (_stateLock)
            {
                _globalAttributes[key] = value;
            }
        }

        public void RemoveAttribute(string key)
        {
            if (key == null)
            {
                return;
            }

            lock (_stateLock)
            {
                _globalAttributes.Remove(key);
            }
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
            if (key == null)
            {
                return;
            }

            Dictionary<string, object> featureFlagsSnapshot;
            lock (_stateLock)
            {
                if (_viewId == null)
                {
                    return;
                }

                _featureFlags[key] = value;
                _viewDocumentVersion++;
                featureFlagsSnapshot = new Dictionary<string, object>(_featureFlags);
            }

            SendViewEvent(attributes: null, featureFlags: featureFlagsSnapshot);
        }

        public void StopSession()
        {
            lock (_stateLock)
            {
                _sessionId = null;
                _viewId = null;
                _viewKey = null;
                _viewName = null;
            }
        }

        public void UpdateExternalRefreshRate(double frameTimeSeconds)
        {
            if (frameTimeSeconds <= 0)
            {
                return;
            }

            var fps = 1.0 / frameTimeSeconds;

            lock (_stateLock)
            {
                if (_viewId == null)
                {
                    return;
                }

                _refreshRateSum += fps;
                _refreshRateSampleCount++;

                if (fps < _refreshRateMin)
                {
                    _refreshRateMin = fps;
                }
            }
        }

        // Called from the main thread by DatadogDesktopLongTaskTracker. The event payload is built under
        // _stateLock so reads of view/session/global-attribute state cannot tear against worker-thread
        // mutations; the synchronous HTTP send is dispatched outside the lock so it does not stall the frame.
        internal void AddLongTask(long durationNs)
        {
            Dictionary<string, object> rumEvent;
            lock (_stateLock)
            {
                if (_viewId == null)
                {
                    return;
                }

                rumEvent = CreateBaseEvent("long_task");
                rumEvent["long_task"] = new Dictionary<string, object>
                {
                    { "id", Guid.NewGuid().ToString() },
                    { "duration", durationNs },
                };

                InjectViewContext(rumEvent);
                MergeAttributes(rumEvent, null);
            }

            System.Threading.Tasks.Task.Run(() => SendRumEvent(rumEvent));
        }

        private Dictionary<string, object> CreateBaseEvent(string type)
        {
            var rumEvent = new Dictionary<string, object>
            {
                { "type", type },
                { "date", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                { "source", "unity" },
                {
                    "application",
                    new Dictionary<string, object> { { "id", _options.RumApplicationId } }
                },
                {
                    "session",
                    new Dictionary<string, object>
                    {
                        { "id", _sessionId ?? EnsureSession() },
                        { "type", "user" },
                    }
                },
                {
                    "_dd",
                    new Dictionary<string, object> { { "format_version", 2 } }
                },
                { "service", _options.ServiceName ?? "unity" },
                { "version", Application.version },
            };

            var env = string.IsNullOrEmpty(_options.Env) ? "prod" : _options.Env;
            rumEvent["env"] = env;

            // Inject user info
            var user = _platform.SnapshotUserInfo();
            var usr = new Dictionary<string, object>();
            if (user.Id != null)
                usr["id"] = user.Id;
            if (user.Name != null)
                usr["name"] = user.Name;
            if (user.Email != null)
                usr["email"] = user.Email;
            foreach (var kvp in user.ExtraInfo)
            {
                usr[kvp.Key] = kvp.Value;
            }

            if (usr.Count > 0)
            {
                rumEvent["usr"] = usr;
            }

            return rumEvent;
        }

        private void InjectViewContext(Dictionary<string, object> rumEvent)
        {
            if (_viewId != null)
            {
                rumEvent["view"] = new Dictionary<string, object>
                {
                    { "id", _viewId },
                    { "url", _viewKey },
                    { "name", _viewName },
                };
            }
        }

        private void MergeAttributes(
            Dictionary<string, object> rumEvent,
            Dictionary<string, object> eventAttributes
        )
        {
            // Snapshot global RUM attributes under the lock so iteration cannot race with
            // AddAttribute/RemoveAttribute on a different thread.
            Dictionary<string, object> globalSnapshot;
            lock (_stateLock)
            {
                globalSnapshot = _globalAttributes.Count > 0
                    ? new Dictionary<string, object>(_globalAttributes)
                    : null;
            }

            if (globalSnapshot != null)
            {
                if (eventAttributes != null)
                {
                    foreach (var kvp in eventAttributes)
                    {
                        globalSnapshot[kvp.Key] = kvp.Value;
                    }
                }

                rumEvent["context"] = globalSnapshot;
            }
            else if (eventAttributes != null && eventAttributes.Count > 0)
            {
                rumEvent["context"] = eventAttributes;
            }
        }

        private string EnsureSession()
        {
            lock (_stateLock)
            {
                _sessionId = Guid.NewGuid().ToString();
                return _sessionId;
            }
        }

        private void SendRumEvent(Dictionary<string, object> rumEvent)
        {
            if (_platform.TrackingConsent != TrackingConsent.Granted)
            {
                return;
            }

            var endpoint = string.IsNullOrEmpty(_options.CustomEndpoint)
                ? DatadogDesktopHelpers.GetRumEndpoint(_options.Site)
                : _options.CustomEndpoint;

            // Matches dd-sdk-android's RumRequestFactory: ddsource as query param,
            // auth and EVP metadata as headers, body as newline-delimited JSON,
            // Content-Type text/plain;charset=UTF-8.
            var url = $"{endpoint}/api/v2/rum?ddsource=unity";
            var jsonPayload = JsonConvert.SerializeObject(rumEvent);

            try
            {
                var content = new StringContent(jsonPayload, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
                {
                    CharSet = "UTF-8",
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content,
                };
                request.Headers.Add("DD-EVP-ORIGIN", "unity");
                request.Headers.Add("DD-EVP-ORIGIN-VERSION", DatadogSdk.SdkVersion);
                request.Headers.Add("DD-REQUEST-ID", Guid.NewGuid().ToString());

                // DD-API-KEY is already set globally on the shared HttpClient.

                using var response = _platform.HttpClient.SendAsync(request).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    var body = response.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    UnityEngine.Debug.LogWarning(
                        $"[Datadog] Failed to send RUM event: {(int)response.StatusCode} {response.StatusCode}. Body: {body}"
                    );
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Datadog] Failed to send RUM event: {e}");
            }
        }
    }
}
