using System.Runtime.CompilerServices;

// The host's own internals — how the log is composed, how a correlation id is accepted — are
// implementation, not surface. Their tests still need to reach them, and asserting on sink
// selection through a running host would mean starting one per case.
[assembly: InternalsVisibleTo("Nelsar.Axia.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
