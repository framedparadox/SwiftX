using Xunit;

namespace SwiftX.Tests;

/// <summary>
/// The xUnit collection every test class that touches <c>orbit.json</c> or
/// <c>secure-values.json</c> belongs to, so those classes run one at a time instead of on top of
/// each other.
/// <para>
/// <b>This is a correctness fix, not a tidiness one.</b> <see cref="TestEnvironment"/> redirects
/// the whole assembly onto one throwaway directory, and it has to: the stores resolve their paths
/// into <c>static readonly</c> fields the first time either type is touched, so there is exactly
/// one <c>orbit.json</c> and one <c>secure-values.json</c> per test process and no per-class
/// directory is reachable without changing the product. xUnit's default, meanwhile, is to run
/// separate test classes in parallel. Those two facts together mean seven classes were writing
/// whole-file saves over each other's fixtures.
/// </para>
/// <para>
/// It showed up as two tests that failed on some runs and passed on others:
/// <list type="bullet">
/// <item><c>SecureValues_WithAMalformedFile_LoadEmptyRatherThanThrowing</c> wrote garbage to the
/// store and asserted that loading it produced nothing — and got back a list of
/// <c>KEY_19_*</c> rows, because <c>ConcurrencyAndEnduranceTests</c> had saved real entries over
/// the garbage between the write and the assert.</item>
/// <item><c>BothStores_SavedFromManyThreadsAtOnce_NeverPublishATornFile</c> saturates both stores
/// from every core it can get, which is precisely the load that makes every other class's use of
/// the same two files lose its own race.</item>
/// </list>
/// A flaky suite is worse than a slow one: these two spent their credibility on timing rather than
/// on the invariants they were written to pin, and the next real regression in either store would
/// have been read as the same old flake.
/// </para>
/// <para>
/// Deliberately one collection rather than <c>parallelizeTestCollections: false</c> across the
/// assembly: only these classes contend for the two files, and the format, syntax, geometry and
/// localization suites are pure functions over their own inputs with no reason to be serialized at
/// all. <c>DisableParallelization</c> then keeps this collection from overlapping the others, which
/// membership alone would not do — the endurance tests in here saturate every core they can get,
/// and <c>Performance.PerformanceBudgetTests</c> asserts wall-clock budgets that a machine busy
/// running them would not meet.
/// </para>
/// </summary>
[CollectionDefinition(SharedDataDirectoryCollection.Name, DisableParallelization = true)]
public sealed class SharedDataDirectoryCollection
{
    /// <summary>The collection name. Referenced by <c>[Collection(...)]</c> on each member class;
    /// a class that starts using either store and forgets this attribute is back in the race, so
    /// it is worth grepping for <c>OrbitStore.</c> / <c>SecureValueStore.</c> when adding one.</summary>
    public const string Name = "SwiftX shared data directory";
}

/// <summary>
/// The collection for tests that move <see cref="SwiftX.Services.Loc"/>'s active language, which
/// is process-wide mutable state.
/// <para>
/// <c>LocTests</c> already restored the language in a <c>finally</c>, and its class comment says
/// why — "leaving it pointed at whatever a test last selected would leak into whichever test (in
/// this class or another) runs next". That reasoning covers the <em>sequential</em> leak and stops
/// exactly one case short of the one that actually bites: <c>GreetingTests</c> calls
/// <c>Loc.Initialize</c> too, it is a different class, and different classes run in parallel. A
/// <c>finally</c> cannot restore state that another thread is changing underneath the assertion.
/// </para>
/// <para>
/// It showed as <c>Initialize_switches_the_active_table</c> failing on roughly one run in three:
/// the test sets French, and <c>GreetingTests</c> sets something else between that call and the
/// <c>Assert.Equal(AppLanguage.French, Loc.CurrentLanguage)</c> on the next line.
/// </para>
/// </summary>
[CollectionDefinition(LocalizationCollection.Name)]
public sealed class LocalizationCollection
{
    /// <summary>The collection name, referenced by <c>[Collection(...)]</c>. Any test that calls
    /// <c>Loc.Initialize</c> or asserts on a localized string belongs here.</summary>
    public const string Name = "SwiftX localization";
}

/// <summary>
/// The collection for tests that assert on wall-clock time, which is to say
/// <c>Performance.PerformanceBudgetTests</c>.
/// <para>
/// A timing assertion run in parallel with other tests is partly a measurement of how many cores
/// those other tests took, which is neither what it claims to measure nor something a regression
/// would change. <c>DisableParallelization</c> gives the suite the machine to itself for the
/// duration — the one condition under which "this got slower" means what it says.
/// </para>
/// <para>
/// It does not make timing assertions reliable on its own, and it is not meant to: the scaling
/// tests also had to stop comparing a cache-resident baseline against an out-of-cache one before
/// they stopped failing on unchanged code. See
/// <c>PerformanceBudgetTests.JsonPipeline_CostGrowsWithTheInput_NotItsSquare</c>.
/// </para>
/// </summary>
[CollectionDefinition(TimingSensitiveCollection.Name, DisableParallelization = true)]
public sealed class TimingSensitiveCollection
{
    /// <summary>The collection name, referenced by <c>[Collection(...)]</c>.</summary>
    public const string Name = "SwiftX timing sensitive";
}
