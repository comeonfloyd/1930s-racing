namespace RacingSim;

/// <summary>
/// Seeds for <see cref="Random"/>. .NET's seeded Random gives closely related sequences for
/// nearby seeds, so seeds such as "race seed + car number" are scrambled here first; without
/// that, every car on a grid could fail on the same lap.
/// </summary>
public static class Seeds
{
    /// <summary>A well-spread seed from two numbers (the MurmurHash3 finaliser).</summary>
    public static int Mix(int a, int b)
    {
        unchecked
        {
            var h = (uint)a * 0x9E3779B1u ^ (uint)b;
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
