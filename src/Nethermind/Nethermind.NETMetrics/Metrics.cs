﻿using System.ComponentModel;

namespace Nethermind.NETMetrics;

public class Metrics
{
    public static IDictionary<string, double> SystemRuntimeMetric = new Dictionary<string, double>();
    public static IDictionary<string, double> DotNETRuntimeMetric = new Dictionary<string, double>();

}
