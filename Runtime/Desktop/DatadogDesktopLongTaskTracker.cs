// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEngine;

namespace Datadog.Unity.Desktop
{
    // Desktop-only long task detector. The native iOS, Android, and Browser SDKs track long tasks themselves
    // (longTaskThreshold / trackLongTasks / trackLongTasks); the desktop platform has no equivalent, so we detect
    // them here from the main thread and forward to DatadogDesktopRum.
    [AddComponentMenu("")]
    internal class DatadogDesktopLongTaskTracker : MonoBehaviour
    {
        // Matches the browser SDK default (100ms).
        private const float LongTaskThresholdSeconds = 0.1f;

        private DatadogDesktopRum _rum;

        public void Init(DatadogDesktopRum rum)
        {
            _rum = rum;
        }

        private void Update()
        {
            if (_rum == null)
            {
                return;
            }

            float frameTime = Time.unscaledDeltaTime;
            if (frameTime > LongTaskThresholdSeconds)
            {
                long durationNs = (long)(frameTime * 1_000_000_000.0);
                _rum.AddLongTask(durationNs);
            }
        }
    }
}
