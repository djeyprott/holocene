using System.Runtime.CompilerServices;

namespace Holocene.Core.Mathx;

/// <summary>
/// Deterministic, counter-based random numbers. A stream is identified by a key built from
/// (run seed, subsystem, entity, time …); the same key always yields the same sequence, independent
/// of thread scheduling or the order in which entities are processed. This is what makes a run
/// reproducible (design principle 3).
/// </summary>
public struct Rng
{
    private ulong _state;
    private readonly ulong _stream;

    public Rng(ulong key)
    {
        _stream = Mix(key ^ 0x9E3779B97F4A7C15UL);
        _state = Mix(key + 0xD1B54A32D192ED03UL);
    }

    /// <summary>Creates a stream keyed by any number of components (seed, subsystem id, entity id, year …).</summary>
    public static Rng For(ulong seed, params long[] parts)
    {
        ulong k = Mix(seed);
        foreach (var p in parts) k = Mix(k ^ (ulong)p) + 0x632BE59BD9B4E019UL;
        return new Rng(k);
    }

    /// <summary>Stable 64-bit hash of a string, used to turn subsystem names into stream keys.</summary>
    public static long HashName(string s)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return (long)Mix(h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong z)
    {
        // SplitMix64 finaliser
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state ^ _stream);
    }

    /// <summary>Uniform in [0,1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public int NextInt(int maxExclusive) => (int)((NextULong() >> 33) % (ulong)maxExclusive);

    /// <summary>Standard normal deviate (Box–Muller).</summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble(), u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    public bool Chance(double p) => NextDouble() < p;
}
