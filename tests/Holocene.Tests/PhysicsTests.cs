using Holocene.Core.Climate;
using Holocene.Core.World;

namespace Holocene.Tests;

public static class PhysicsTests
{
    public static void TestOrbitMatchesReferenceValues()
    {
        // J2000 astronomical values: e = 0.016709, obliquity 23.4393°, longitude of perihelion from the moving
        // equinox (Berger's ϖ, i.e. heliocentric 282.9° − 180°) ≈ 102.9°.
        var o = Orbital.ForYear(2000);
        Assert.Near(o.Eccentricity, 0.016709, 3e-5, "eccentricity J2000");
        Assert.Near(o.ObliquityRad * 180 / Math.PI, 23.4393, 3e-3, "obliquity J2000");
        Assert.Near(o.PerihelionDeg, 102.9, 0.3, "perihelion longitude J2000");
        // Early Holocene: higher obliquity (~24.2°) and perihelion near northern summer solstice (ϖ ≈ 270°).
        var h = Orbital.ForYear(-9999);
        Assert.True(h.ObliquityRad * 180 / Math.PI > 24.0, "early Holocene obliquity > 24°");
        Assert.True(h.PerihelionDeg > 240 && h.PerihelionDeg < 300, $"early Holocene perihelion {h.PerihelionDeg}");
    }

    public static void TestGlobalAnnualInsolation()
    {
        // Global, annual mean insolation must equal S/4 · (1 − e²)^−½.
        foreach (double year in new[] { 1990.0, -9999.0, -50000.0 })
        {
            var o = Orbital.ForYear(year);
            double sum = 0, wsum = 0;
            for (int d = 0; d < 365; d++)
            {
                var (dec, fac) = Orbital.Declination(o, d + 1.5);
                for (int l = 0; l < 180; l++)
                {
                    double lat = (-89.5 + l) * Math.PI / 180, w = Math.Cos(lat);
                    sum += Orbital.DailyInsolation(1361, lat, dec, fac) * w; wsum += w;
                }
            }
            double expected = 1361 / 4.0 / Math.Sqrt(1 - o.Eccentricity * o.Eccentricity);
            Assert.Near(sum / wsum, expected, 0.3, $"global mean insolation {year}");
        }
    }

    public static void TestSummerInsolationAt65NWasHigherInEarlyHolocene()
    {
        // Milankovitch: midsummer-day insolation at 65°N around 11 ka was ~40–50 W/m² above today (Berger 1978).
        double Solstice(double year)
        {
            var o = Orbital.ForYear(year); var (dec, fac) = Orbital.Declination(o, 172);
            return Orbital.DailyInsolation(1361, 65 * Math.PI / 180, dec, fac);
        }
        double diff = Solstice(-9049) - Solstice(1950);
        Assert.True(diff > 35 && diff < 60, $"21 June 65N insolation anomaly at 11 ka: {diff:F1} W/m²");
    }

    public static void TestKoppenClassification()
    {
        double[] Mm(params double[] perDay) => perDay.Select(x => x * 30.4).ToArray();
        // London: mild winters, cool summers, rain all year → Cfb
        Assert.True(Biomes.Koppen(new[] { 5.2, 5.3, 7.6, 9.9, 13.3, 16.5, 18.7, 18.5, 15.7, 12.0, 8.0, 5.5 }, Mm(1.8, 1.4, 1.3, 1.5, 1.6, 1.5, 1.5, 1.6, 1.6, 2.2, 2.0, 1.8), true) == "Cfb", "London Cfb");
        // Cairo: hot desert → BWh
        Assert.True(Biomes.Koppen(new[] { 14.0, 15.0, 17.5, 21.0, 24.5, 27.0, 28.0, 28.0, 26.0, 23.5, 19.5, 15.5 }, Mm(0.2, 0.15, 0.1, 0.03, 0.02, 0, 0, 0, 0, 0.02, 0.1, 0.2), true) == "BWh", "Cairo BWh");
        // Moscow: cold winters, warm summers, rain all year → Dfb
        Assert.True(Biomes.Koppen(new[] { -6.5, -6.7, -1.0, 6.7, 13.2, 17.0, 19.2, 17.0, 11.3, 5.6, -0.2, -4.5 }, Mm(1.7, 1.3, 1.1, 1.2, 1.7, 2.6, 2.7, 2.5, 2.2, 2.0, 1.7, 1.6), true) == "Dfb", "Moscow Dfb");
        // Manaus: equatorial, wet → Af
        Assert.True(Biomes.Koppen(new[] { 26.1, 26.0, 26.2, 26.3, 26.5, 26.6, 26.9, 27.5, 27.9, 27.7, 27.3, 26.7 }, Mm(9.7, 10.4, 10.3, 11.0, 8.6, 3.5, 2.8, 1.6, 2.5, 3.8, 6.0, 8.4), false).StartsWith("A"), "Manaus tropical");
        // Arctic: warmest month below 10 °C → ET
        Assert.True(Biomes.Koppen(new[] { -30.0, -30, -28, -20, -8, 2, 6, 4, -2, -12, -22, -28 }, Mm(0.4, 0.4, 0.4, 0.4, 0.4, 0.6, 1.0, 1.2, 0.8, 0.6, 0.4, 0.4), true) == "ET", "tundra ET");
    }

    public static void TestMiamiNpp()
    {
        Assert.True(Biomes.MiamiNpp(26, 2500) > 2000, "tropical rainforest NPP > 2000 g/m²/yr");
        Assert.True(Biomes.MiamiNpp(25, 50) < 150, "desert NPP < 150");
        Assert.True(Biomes.MiamiNpp(-10, 300) < 400, "tundra NPP low");
    }

    public static void TestSaturationHumidity()
    {
        // Bolton: e_s(20 °C) = 23.4 hPa; q_s at 1013 hPa ≈ 14.5 g/kg
        Assert.Near(Thermo.Esat(20), 23.37, 0.05, "e_s(20C)");
        Assert.Near(Thermo.Qsat(20, 1013.25) * 1000, 14.5, 0.2, "q_s(20C)");
    }
}
