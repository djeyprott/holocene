using System.Runtime.CompilerServices;

namespace Holocene.Core.Mathx;

/// <summary>Double-precision 3-vector used for positions on the unit sphere.</summary>
public readonly struct Vec3
{
    public readonly double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vec3 Normalized() { double l = Length; return new Vec3(X / l, Y / l, Z / l); }

    /// <summary>Latitude in radians of a unit vector (Z is the rotation axis).</summary>
    public double Lat => Math.Asin(Math.Clamp(Z, -1.0, 1.0));
    /// <summary>Longitude in radians, (-π, π].</summary>
    public double Lon => Math.Atan2(Y, X);

    public static Vec3 FromLatLon(double latRad, double lonRad)
    {
        double c = Math.Cos(latRad);
        return new Vec3(c * Math.Cos(lonRad), c * Math.Sin(lonRad), Math.Sin(latRad));
    }

    /// <summary>Great-circle angle between two unit vectors (radians), numerically stable for small angles.</summary>
    public static double Angle(Vec3 a, Vec3 b) => Math.Atan2(Cross(a, b).Length, Dot(a, b));

    /// <summary>Local unit east vector at a point on the unit sphere (undefined exactly at the poles).</summary>
    public static Vec3 East(Vec3 p)
    {
        var e = new Vec3(-p.Y, p.X, 0);
        double l = e.Length;
        return l < 1e-12 ? new Vec3(0, 1, 0) : e / l;
    }

    /// <summary>Local unit north vector at a point on the unit sphere.</summary>
    public static Vec3 North(Vec3 p) => Cross(p, East(p));

    public override string ToString() => $"({X:F5},{Y:F5},{Z:F5})";
}

public static class Sphere
{
    /// <summary>Area of the spherical triangle abc on the unit sphere (steradians), via L'Huilier-free stable formula.</summary>
    public static double TriangleArea(Vec3 a, Vec3 b, Vec3 c)
    {
        // Van Oosterom & Strackee (1983): tan(Ω/2) = |a·(b×c)| / (1 + a·b + b·c + c·a)
        double num = Math.Abs(Vec3.Dot(a, Vec3.Cross(b, c)));
        double den = 1 + Vec3.Dot(a, b) + Vec3.Dot(b, c) + Vec3.Dot(c, a);
        return 2 * Math.Atan2(num, den);
    }

    /// <summary>Circumcenter of a spherical triangle, projected to the unit sphere, on the same side as the triangle.</summary>
    public static Vec3 Circumcenter(Vec3 a, Vec3 b, Vec3 c)
    {
        var n = Vec3.Cross(b - a, c - a).Normalized();
        return Vec3.Dot(n, a + b + c) < 0 ? -n : n;
    }
}
