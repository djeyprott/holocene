namespace Holocene.Core.Climate;

/// <summary>Moist thermodynamics helpers.</summary>
public static class Thermo
{
    /// <summary>Saturation vapour pressure over water (hPa), Bolton (1980); T in °C.</summary>
    public static double Esat(double tC) => 6.112 * Math.Exp(17.67 * tC / (tC + 243.5));

    /// <summary>Saturation specific humidity (kg/kg) at T (°C) and pressure p (hPa).</summary>
    public static double Qsat(double tC, double pHpa)
    {
        double es = Esat(tC);
        return 0.622 * es / Math.Max(pHpa - 0.378 * es, 1.0);
    }

    /// <summary>dqsat/dT (1/K) from the Clausius–Clapeyron form of Bolton's fit.</summary>
    public static double DQsatDT(double tC, double pHpa) => Qsat(tC, pHpa) * 17.67 * 243.5 / ((tC + 243.5) * (tC + 243.5));

    /// <summary>Surface pressure (hPa) at elevation z (m) using an 8 km scale height.</summary>
    public static double PressureAt(double z) => 1013.25 * Math.Exp(-Math.Max(z, 0) / 8000.0);

    /// <summary>Open-ocean albedo as a function of mean cosine zenith angle (Briegleb et al. 1986, used in CCM3).</summary>
    public static double OceanAlbedo(double mu)
    {
        if (mu <= 0.01) return 0.4;
        return Math.Clamp(0.026 / (Math.Pow(mu, 1.7) + 0.065) + 0.15 * (mu - 0.1) * (mu - 0.5) * (mu - 1.0), 0.03, 0.4);
    }
}
