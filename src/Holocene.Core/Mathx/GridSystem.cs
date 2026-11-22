using Holocene.Core.Grid;

namespace Holocene.Core.Mathx;

/// <summary>
/// Sparse linear system whose sparsity pattern is the grid's neighbour graph:
/// row i = Diag[i]·x[i] + Σ_k Off[k]·x[Nbr[k]] for k in the neighbour range of i.
/// Solved with Jacobi-preconditioned BiCGSTAB (matrices here are non-symmetric because of upwind advection).
/// </summary>
public sealed class GridSystem
{
    private readonly GeodesicGrid _g;
    public readonly double[] Diag;
    public readonly double[] Off;
    public readonly double[] Rhs;

    public GridSystem(GeodesicGrid g)
    {
        _g = g;
        Diag = new double[g.Count];
        Off = new double[g.Nbr.Length];
        Rhs = new double[g.Count];
    }

    public void Clear()
    {
        Array.Clear(Diag); Array.Clear(Off); Array.Clear(Rhs);
    }

    /// <summary>Adds a symmetric exchange term a·(x_i − x_j) to row i and a·(x_j − x_i) to row j, for edge entry k of cell i.</summary>
    public void AddExchange(int i, int k, double a)
    {
        int j = _g.Nbr[k];
        Diag[i] += a; Off[k] -= a;
        Diag[j] += a; Off[_g.EdgeReverse[k]] -= a;
    }

    public void Multiply(double[] x, double[] y)
    {
        var nbr = _g.Nbr; var start = _g.NbrStart;
        Parallel.For(0, _g.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            double s = Diag[i] * x[i];
            for (int k = start[i]; k < start[i + 1]; k++) s += Off[k] * x[nbr[k]];
            y[i] = s;
        });
    }

    /// <summary>Solves in place starting from x. Returns (iterations, relative residual).</summary>
    public (int iterations, double residual) Solve(double[] x, double tol = 1e-8, int maxIter = 2000)
    {
        int n = _g.Count;
        var r = new double[n]; var r0 = new double[n]; var p = new double[n]; var v = new double[n];
        var s = new double[n]; var t = new double[n]; var ph = new double[n]; var sh = new double[n];
        var inv = new double[n];
        for (int i = 0; i < n; i++) inv[i] = Diag[i] != 0 ? 1.0 / Diag[i] : 1.0;

        Multiply(x, r);
        double bnorm = 0;
        for (int i = 0; i < n; i++) { r[i] = Rhs[i] - r[i]; r0[i] = r[i]; bnorm += Rhs[i] * Rhs[i]; }
        bnorm = Math.Sqrt(bnorm); if (bnorm == 0) bnorm = 1;
        double rho = 1, alpha = 1, omega = 1;
        double res = Norm(r) / bnorm;
        if (res < tol) return (0, res);
        for (int it = 1; it <= maxIter; it++)
        {
            double rhoNew = Dot(r0, r);
            if (rhoNew == 0) return (it, res);
            double beta = rhoNew / rho * (alpha / omega);
            rho = rhoNew;
            for (int i = 0; i < n; i++) p[i] = r[i] + beta * (p[i] - omega * v[i]);
            for (int i = 0; i < n; i++) ph[i] = p[i] * inv[i];
            Multiply(ph, v);
            alpha = rho / Dot(r0, v);
            for (int i = 0; i < n; i++) s[i] = r[i] - alpha * v[i];
            if (Norm(s) / bnorm < tol)
            {
                for (int i = 0; i < n; i++) x[i] += alpha * ph[i];
                return (it, Norm(s) / bnorm);
            }
            for (int i = 0; i < n; i++) sh[i] = s[i] * inv[i];
            Multiply(sh, t);
            omega = Dot(t, s) / Dot(t, t);
            for (int i = 0; i < n; i++) { x[i] += alpha * ph[i] + omega * sh[i]; r[i] = s[i] - omega * t[i]; }
            res = Norm(r) / bnorm;
            if (res < tol) return (it, res);
            if (omega == 0) return (it, res);
        }
        return (maxIter, res);
    }

    private static double Dot(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }
    private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
}
