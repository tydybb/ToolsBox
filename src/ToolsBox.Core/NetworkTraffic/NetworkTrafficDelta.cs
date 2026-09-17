namespace ToolsBox.Core.NetworkTraffic;

public sealed record NetworkTrafficDelta(
    int ProcessId,
    DateTimeOffset? ProcessStartedAt,
    NetworkTrafficDirection Direction,
    long ByteCount,
    DateTimeOffset Timestamp);
