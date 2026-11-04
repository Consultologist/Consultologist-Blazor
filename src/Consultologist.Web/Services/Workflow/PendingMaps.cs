namespace Consultologist.Web.Services.Workflow;

/// <summary>#1225: helpers over the editor's pending maps (a generic method cannot live in a Razor code block).</summary>
public static class PendingMaps
{
    /// <summary>A map keyed by node id re-keyed from <paramref name="from"/> to <paramref name="to"/>, every value mapped.</summary>
    /// <summary>#1290: the entry under <paramref name="from"/> moved to <paramref name="to"/>, its value kept; nothing when absent.</summary>
    public static void Move<TValue>(Dictionary<string, TValue> map, string from, string to)
    {
        if (map.Remove(from, out var value))
        {
            map[to] = value;
        }
    }

    public static void Rekey<TValue>(Dictionary<string, TValue> map, string from, string to, Func<TValue, TValue> value)
    {
        foreach (var key in map.Keys.ToList())
        {
            var renamed = value(map[key]);
            map.Remove(key);
            map[key == from ? to : key] = renamed;
        }
    }
}
