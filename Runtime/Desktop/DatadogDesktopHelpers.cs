// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity.Logs;

namespace Datadog.Unity.Desktop
{
    internal static class DatadogDesktopHelpers
    {
        internal static string GetLogsEndpoint(DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => "https://http-intake.logs.datadoghq.com",
                DatadogSite.Us3 => "https://http-intake.logs.us3.datadoghq.com",
                DatadogSite.Us5 => "https://http-intake.logs.us5.datadoghq.com",
                DatadogSite.Eu1 => "https://http-intake.logs.datadoghq.eu",
                DatadogSite.Us1Fed => "https://http-intake.logs.ddog-gov.com",
                DatadogSite.Ap1 => "https://http-intake.logs.ap1.datadoghq.com",
                DatadogSite.Ap2 => "https://http-intake.logs.ap2.datadoghq.com",
                _ => "https://http-intake.logs.datadoghq.com",
            };
        }

        internal static string DdLogLevelToStatus(DdLogLevel level)
        {
            return level switch
            {
                DdLogLevel.Debug => "debug",
                DdLogLevel.Info => "info",
                DdLogLevel.Notice => "notice",
                DdLogLevel.Warn => "warn",
                DdLogLevel.Error => "error",
                DdLogLevel.Critical => "critical",
                _ => "info",
            };
        }
    }
}
