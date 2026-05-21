using Xunit;

namespace HealthMailer.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessHandleMeasurementCollection
{
    public const string Name = "Process handle measurement";
}
