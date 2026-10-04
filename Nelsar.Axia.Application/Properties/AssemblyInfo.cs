using System.Runtime.CompilerServices;

// The layer's handlers, validators, behaviours and collaborators are internal — they are how the
// use cases are implemented, not what they offer. Its tests still need to reach them, and testing
// through the public surface alone would mean driving every one of them through MediatR.
[assembly: InternalsVisibleTo("Nelsar.Axia.Tests")]

// Moq builds its dynamic proxies here; without it, mocking an internal type fails at run time.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
