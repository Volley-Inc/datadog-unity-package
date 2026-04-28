// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity
{
    internal static class DatadogIntakeEndpoints
    {
        // See DataDog/browser-sdk/packages/core/src/domain/configuration/endpointBuilder.ts
        // Browser intake host is derived from the site domain: domain parts joined with '-',
        // then prefixed with 'browser-intake-'.
        public static string GetBrowserIntakeEndpoint(DatadogSite site)
        {
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
    }
}
