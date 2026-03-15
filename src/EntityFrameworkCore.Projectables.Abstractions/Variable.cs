namespace EntityFrameworkCore.Projectables;

/// <summary>
/// Utility class for marking reused local variables in projectable expression trees,
/// enabling the SQL generator to hoist shared computations into <c>CROSS APPLY</c> (SQL Server)
/// or <c>CROSS JOIN LATERAL</c> (PostgreSQL) inline subqueries.
/// </summary>
public static class Variable
{
    /// <summary>
    /// Identity function that marks a reused local variable in a generated expression tree.
    /// <para>
    /// When the same <paramref name="name"/> appears more than once in a generated
    /// expression tree (because the corresponding local variable was referenced multiple times
    /// in a <c>[Projectable(AllowBlockBody = true)]</c> method body), the SQL generator hoists
    /// the shared computation into a single inline subquery evaluated exactly once per row:
    /// <code>
    /// -- SQL Server
    /// CROSS APPLY (SELECT &lt;inner expression&gt; AS [name]) AS [v]
    ///
    /// -- PostgreSQL
    /// CROSS JOIN LATERAL (SELECT &lt;inner expression&gt; AS "name") AS "v"
    /// </code>
    /// </para>
    /// <para>
    /// At runtime this method is a pure identity function: it returns
    /// <paramref name="value"/> unchanged and has no observable effect.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="name">
    /// The original local variable name, used to correlate multiple uses of the same
    /// computation within a single expression tree.
    /// </param>
    /// <param name="value">The value to pass through unchanged.</param>
    /// <returns><paramref name="value"/> unchanged.</returns>
    public static T Wrap<T>(string name, T value) => value;
}
