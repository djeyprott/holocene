namespace Holocene.Core.Climate;

/// <summary>Earth's orbital elements for a given year.</summary>
/// <param name="Eccentricity">orbital eccentricity</param>
/// <param name="ObliquityRad">axial tilt, radians</param>
/// <param name="PerihelionDeg">longitude of perihelion relative to the moving vernal equinox, degrees (Berger's ϖ, 0–360)</param>
public readonly record struct OrbitalElements(double Eccentricity, double ObliquityRad, double PerihelionDeg)
{
    /// <summary>Longitude of perihelion plus π (radians), as used by the declination formula.</summary>
    public double MvelpP => (PerihelionDeg + 180.0) * Math.PI / 180.0;

    /// <summary>Mean longitude at the vernal equinox (Berger 1978).</summary>
    public double LambM0
    {
        get
        {
            double e = Eccentricity, e2 = e * e, e3 = e2 * e, beta = Math.Sqrt(1 - e2), m = MvelpP;
            return 2 * ((0.5 * e + 0.125 * e3) * (1 + beta) * Math.Sin(m)
                        - 0.25 * e2 * (0.5 + beta) * Math.Sin(2 * m)
                        + 0.125 * e3 * (1.0 / 3.0 + beta) * Math.Sin(3 * m));
        }
    }

    /// <summary>Climatic precession index e·sin(ϖ).</summary>
    public double PrecessionIndex => Eccentricity * Math.Sin(PerihelionDeg * Math.PI / 180);
}

/// <summary>
/// Orbital forcing after Berger (1978), "Long-term variations of daily insolation and Quaternary climatic
/// changes", J. Atmos. Sci. 35, 2362–2367. Series coefficients and algorithm ported from NCAR CESM
/// shr_orb_mod.F90 (valid ±1 Myr around 1950 CE). Days use a 365-day year with the vernal equinox on
/// day 80.5 (21 March, Jan 1 = day 1), as in CESM.
/// </summary>
public static class Orbital
{
    public const double DaysPerYear = 365.0;
    public const double VernalEquinoxDay = 80.5;

    /// <summary>Orbital elements for a calendar year (astronomical numbering: 1 BCE = 0, 10,000 BCE = −9999).</summary>
    public static OrbitalElements ForYear(double yearCE)
    {
        const double psecdeg = 1.0 / 3600.0, degrad = Math.PI / 180.0;
        double years = yearCE - 1950.0;
        double obsum = 0;
        for (int i = 0; i < Obamp.Length; i++) obsum += Obamp[i] * psecdeg * Math.Cos((Obrate[i] * psecdeg * years + Obphas[i]) * degrad);
        double obliq = 23.320556 + obsum;

        double cossum = 0, sinsum = 0;
        for (int i = 0; i < Ecamp.Length; i++)
        {
            double arg = (Ecrate[i] * psecdeg * years + Ecphas[i]) * degrad;
            cossum += Ecamp[i] * Math.Cos(arg); sinsum += Ecamp[i] * Math.Sin(arg);
        }
        double eccen = Math.Sqrt(cossum * cossum + sinsum * sinsum);
        double fvelp = Math.Atan2(sinsum, cossum); if (fvelp < 0) fvelp += 2 * Math.PI;

        double mvsum = 0;
        for (int i = 0; i < Mvamp.Length; i++) mvsum += Mvamp[i] * psecdeg * Math.Sin((Mvrate[i] * psecdeg * years + Mvphas[i]) * degrad);
        double mvelp = fvelp / degrad + 50.439273 * psecdeg * years + 3.392506 + mvsum;
        mvelp %= 360.0; if (mvelp < 0) mvelp += 360.0;
        return new OrbitalElements(eccen, obliq * degrad, mvelp);
    }

    /// <summary>Solar declination (rad) and (1/ρ)² distance factor on a calendar day (1-based, fractional).</summary>
    public static (double declination, double distanceFactor) Declination(in OrbitalElements o, double calday)
    {
        double e = o.Eccentricity, m = o.MvelpP;
        double lambm = o.LambM0 + (calday - VernalEquinoxDay) * 2 * Math.PI / DaysPerYear;
        double lmm = lambm - m, sinl = Math.Sin(lmm);
        double lamb = lambm + e * (2 * sinl + e * (1.25 * Math.Sin(2 * lmm) + e * (13.0 / 12.0 * Math.Sin(3 * lmm) - 0.25 * sinl)));
        double invrho = (1 + e * Math.Cos(lamb - m)) / (1 - e * e);
        return (Math.Asin(Math.Sin(o.ObliquityRad) * Math.Sin(lamb)), invrho * invrho);
    }

    /// <summary>Daily-mean top-of-atmosphere insolation (W/m²) at latitude φ (rad).</summary>
    public static double DailyInsolation(double solarConstant, double latRad, double declination, double distanceFactor)
    {
        double x = -Math.Tan(latRad) * Math.Tan(declination);
        double h0 = x >= 1 ? 0 : x <= -1 ? Math.PI : Math.Acos(x); // hour angle of sunset
        return solarConstant / Math.PI * distanceFactor *
               (h0 * Math.Sin(latRad) * Math.Sin(declination) + Math.Cos(latRad) * Math.Cos(declination) * Math.Sin(h0));
    }

    /// <summary>Insolation-weighted mean cosine of the solar zenith angle over a day (used for ocean albedo).</summary>
    public static double MeanCosZenith(double latRad, double declination)
    {
        double x = -Math.Tan(latRad) * Math.Tan(declination);
        double h0 = x >= 1 ? 0 : x <= -1 ? Math.PI : Math.Acos(x);
        if (h0 <= 0) return 0;
        double a = Math.Sin(latRad) * Math.Sin(declination), b = Math.Cos(latRad) * Math.Cos(declination);
        // ∫cos²z dh / ∫cos z dh over daylight
        double i1 = a * h0 + b * Math.Sin(h0);
        double i2 = a * a * h0 + 2 * a * b * Math.Sin(h0) + b * b * (h0 / 2 + Math.Sin(2 * h0) / 4);
        return i1 > 0 ? i2 / i1 : 0;
    }

    private static readonly double[] Obamp =
    {
        -2462.2214466, -857.3232075, -629.3231835, -414.2804924, -311.7632587, 308.9408604,
        -162.5533601, -116.1077911, 101.1189923, -67.6856209, 24.9079067, 22.5811241,
        -21.1648355, -15.6549876, 15.3936813, 14.6660938, -11.7273029, 10.2742696,
        6.4914588, 5.8539148, -5.4872205, -5.4290191, 5.1609570, 5.0786314,
        -4.0735782, 3.7227167, 3.3971932, -2.8347004, -2.6550721, -2.5717867,
        -2.4712188, 2.4625410, 2.2464112, -2.0755511, -1.9713669, -1.8813061,
        -1.8468785, 1.8186742, 1.7601888, -1.5428851, 1.4738838, -1.4593669,
        1.4192259, -1.1818980, 1.1756474, -1.1316126, 1.0896928,
    };
    private static readonly double[] Obrate =
    {
        31.609974, 32.620504, 24.172203, 31.983787, 44.828336, 30.973257,
        43.668246, 32.246691, 30.599444, 42.681324, 43.836462, 47.439436,
        63.219948, 64.230478, 1.010530, 7.437771, 55.782177, 0.373813,
        13.218362, 62.583231, 63.593761, 76.438310, 45.815258, 8.448301,
        56.792707, 49.747842, 12.058272, 75.278220, 65.241008, 64.604291,
        1.647247, 7.811584, 12.207832, 63.856665, 56.155990, 77.448840,
        6.801054, 62.209418, 20.656133, 48.344406, 55.145460, 69.000539,
        11.071350, 74.291298, 11.047742, 0.636717, 12.844549,
    };
    private static readonly double[] Obphas =
    {
        251.9025, 280.8325, 128.3057, 292.7252, 15.3747, 263.7951,
        308.4258, 240.0099, 222.9725, 268.7809, 316.7998, 319.6024,
        143.8050, 172.7351, 28.9300, 123.5968, 20.2082, 40.8226,
        123.4722, 155.6977, 184.6277, 267.2772, 55.0196, 152.5268,
        49.1382, 204.6609, 56.5233, 200.3284, 201.6651, 213.5577,
        17.0374, 164.4194, 94.5422, 131.9124, 61.0309, 296.2073,
        135.4894, 114.8750, 247.0691, 256.6114, 32.1008, 143.6804,
        16.8784, 160.6835, 27.5932, 348.1074, 82.6496,
    };
    private static readonly double[] Ecamp =
    {
        0.01860798, 0.01627522, -0.01300660, 0.00988829, -0.00336700, 0.00333077,
        -0.00235400, 0.00140015, 0.00100700, 0.00085700, 0.00064990, 0.00059900,
        0.00037800, -0.00033700, 0.00027600, 0.00018200, -0.00017400, -0.00012400,
        0.00001250,
    };
    private static readonly double[] Ecrate =
    {
        4.2072050, 7.3460910, 17.8572630, 17.2205460, 16.8467330, 5.1990790,
        18.2310760, 26.2167580, 6.3591690, 16.2100160, 3.0651810, 16.5838290,
        18.4939800, 6.1909530, 18.8677930, 17.4255670, 6.1860010, 18.4174410,
        0.6678630,
    };
    private static readonly double[] Ecphas =
    {
        28.620089, 193.788772, 308.307024, 320.199637, 279.376984, 87.195000,
        349.129677, 128.443387, 154.143880, 291.269597, 114.860583, 332.092251,
        296.414411, 145.769910, 337.237063, 152.092288, 126.839891, 210.667199,
        72.108838,
    };
    private static readonly double[] Mvamp =
    {
        7391.0225890, 2555.1526947, 2022.7629188, -1973.6517951, 1240.2321818, 953.8679112,
        -931.7537108, 872.3795383, 606.3544732, -496.0274038, 456.9608039, 346.9462320,
        -305.8412902, 249.6173246, -199.1027200, 191.0560889, -175.2936572, 165.9068833,
        161.1285917, 139.7878093, -133.5228399, 117.0673811, 104.6907281, 95.3227476,
        86.7824524, 86.0857729, 70.5893698, -69.9719343, -62.5817473, 61.5450059,
        -57.9364011, 57.1899832, -57.0236109, -54.2119253, 53.2834147, 52.1223575,
        -49.0059908, -48.3118757, -45.4191685, -42.2357920, -34.7971099, 34.4623613,
        -33.8356643, 33.6689362, -31.2521586, -30.8798701, 28.4640769, -27.1960802,
        27.0860736, -26.3437456, 24.7253740, 24.6732126, 24.4272733, 24.0127327,
        21.7150294, -21.5375347, 18.1148363, -16.9603104, -16.1765215, 15.5567653,
        15.4846529, 15.2150632, 14.5047426, -14.3873316, 13.1351419, 12.8776311,
        11.9867234, 11.9385578, 11.7030822, 11.6018181, -11.2617293, -10.4664199,
        10.4333970, -10.2377466, 10.1934446, -10.1280191, 10.0289441, -10.0034259,
    };
    private static readonly double[] Mvrate =
    {
        31.609974, 32.620504, 24.172203, 0.636717, 31.983787, 3.138886,
        30.973257, 44.828336, 0.991874, 0.373813, 43.668246, 32.246691,
        30.599444, 2.147012, 10.511172, 42.681324, 13.650058, 0.986922,
        9.874455, 13.013341, 0.262904, 0.004952, 1.142024, 63.219948,
        0.205021, 2.151964, 64.230478, 43.836462, 47.439436, 1.384343,
        7.437771, 18.829299, 9.500642, 0.431696, 1.160090, 55.782177,
        12.639528, 1.155138, 0.168216, 1.647247, 10.884985, 5.610937,
        12.658184, 1.010530, 1.983748, 14.023871, 0.560178, 1.273434,
        12.021467, 62.583231, 63.593761, 76.438310, 4.280910, 13.218362,
        17.818769, 8.359495, 56.792707, 8.448301, 1.978796, 8.863925,
        0.186365, 8.996212, 6.771027, 45.815258, 12.002811, 75.278220,
        65.241008, 18.870667, 22.009553, 64.604291, 11.498094, 0.578834,
        9.237738, 49.747842, 2.147012, 1.196895, 2.133898, 0.173168,
    };
    private static readonly double[] Mvphas =
    {
        251.9025, 280.8325, 128.3057, 348.1074, 292.7252, 165.1686,
        263.7951, 15.3747, 58.5749, 40.8226, 308.4258, 240.0099,
        222.9725, 106.5937, 114.5182, 268.7809, 279.6869, 39.6448,
        126.4108, 291.5795, 307.2848, 18.9300, 273.7596, 143.8050,
        191.8927, 125.5237, 172.7351, 316.7998, 319.6024, 69.7526,
        123.5968, 217.6432, 85.5882, 156.2147, 66.9489, 20.2082,
        250.7568, 48.0188, 8.3739, 17.0374, 155.3409, 94.1709,
        221.1120, 28.9300, 117.1498, 320.5095, 262.3602, 336.2148,
        233.0046, 155.6977, 184.6277, 267.2772, 78.9281, 123.4722,
        188.7132, 180.1364, 49.1382, 152.5268, 98.2198, 97.4808,
        221.5376, 168.2438, 161.1199, 55.0196, 262.6495, 200.3284,
        201.6651, 294.6547, 99.8233, 213.5577, 154.1631, 232.7153,
        138.3034, 204.6609, 106.5938, 250.4676, 332.3345, 27.3039,
    };}
