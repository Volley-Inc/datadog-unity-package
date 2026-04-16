// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity.Logs;
using Datadog.Unity.Rum;

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

        internal static string GetRumEndpoint(DatadogSite site)
        {
            // See DataDog/browser-sdk/packages/core/src/domain/configuration/endpointBuilder.ts
            // Browser intake host is derived from the site domain: domain parts joined with '-',
            // then prefixed with 'browser-intake-'.
            return site switch
            {
                DatadogSite.Us1 => "https://browser-intake-datadoghq.com",
                DatadogSite.Us3 => "https://browser-intake-us3-datadoghq.com",
                DatadogSite.Us5 => "https://browser-intake-us5-datadoghq.com",
                DatadogSite.Eu1 => "https://browser-intake-datadoghq.eu",
                DatadogSite.Us1Fed => "https://browser-intake-ddog-gov.com",
                DatadogSite.Ap1 => "https://browser-intake-ap1-datadoghq.com",
                DatadogSite.Ap2 => "https://browser-intake-ap2-datadoghq.com",
                _ => "https://browser-intake-datadoghq.com",
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

        internal static string RumActionTypeToString(RumUserActionType type)
        {
            return type switch
            {
                RumUserActionType.Tap => "tap",
                RumUserActionType.Scroll => "scroll",
                RumUserActionType.Swipe => "swipe",
                RumUserActionType.Custom => "custom",
                _ => "custom",
            };
        }

        internal static string RumErrorSourceToString(RumErrorSource source)
        {
            return source switch
            {
                RumErrorSource.Source => "source",
                RumErrorSource.Network => "network",
                RumErrorSource.WebView => "webview",
                RumErrorSource.Console => "console",
                RumErrorSource.Custom => "custom",
                _ => "source",
            };
        }

        internal static string RumResourceTypeToString(RumResourceType type)
        {
            return type switch
            {
                RumResourceType.Document => "document",
                RumResourceType.Image => "image",
                RumResourceType.Xhr => "xhr",
                RumResourceType.Beacon => "beacon",
                RumResourceType.Css => "css",
                RumResourceType.Fetch => "fetch",
                RumResourceType.Font => "font",
                RumResourceType.Js => "js",
                RumResourceType.Media => "media",
                RumResourceType.Native => "native",
                RumResourceType.Other => "other",
                _ => "other",
            };
        }

        internal static string RumHttpMethodToString(RumHttpMethod method)
        {
            return method switch
            {
                RumHttpMethod.Post => "POST",
                RumHttpMethod.Get => "GET",
                RumHttpMethod.Head => "HEAD",
                RumHttpMethod.Put => "PUT",
                RumHttpMethod.Delete => "DELETE",
                RumHttpMethod.Patch => "PATCH",
                _ => "GET",
            };
        }
    }
}
