using Xunit;

namespace Novalist.Desktop.Tests;

/// <summary>
/// Marker fixture for the shared "Avalonia" collection. The headless Avalonia app is set up
/// by <see cref="AvaloniaTestFramework"/> on the dedicated test thread (so the dispatcher is
/// bound to the same thread every test runs on); this fixture intentionally does no setup.
/// </summary>
public sealed class AvaloniaFixture
{
}

[CollectionDefinition("Avalonia")]
public sealed class AvaloniaCollection : ICollectionFixture<AvaloniaFixture> { }
