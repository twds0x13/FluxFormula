using System;
using FluxFormula.Core;

/// <summary>
/// Aggregated statistics from a multiverse simulation.
/// </summary>
public readonly struct MultiverseStats
{
    public readonly float Avg;
    public readonly float Max;
    public readonly float Min;
    public readonly float Mid;

    public MultiverseStats(float avg, float max, float min, float mid)
    {
        Avg = avg;
        Max = max;
        Min = min;
        Mid = mid;
    }
}

/// <summary>
/// Extension methods for multiverse (fork + simulate) damage evaluation.
/// </summary>
public static class MultiverseExtensions
{
    // ── Private helpers ──

    /// <summary>
    /// Runs <paramref name="count"/> simulations and returns raw results.
    /// <paramref name="nextCrit"/> returns 1f for crit, 0f for no crit on each call.
    /// </summary>
    private static float[] Simulate<TDef>(
        FluxCurryEvaluator<float, TDef> curry,
        string varName, int count,
        Func<float> nextCrit)
        where TDef : unmanaged, IFluxExprDefinition<float>
    {
        var results = new float[count];
        for (int i = 0; i < count; i++)
        {
            var fork = curry.Bind(varName, nextCrit());
            results[i] = fork.ForceComplete().Result;
        }
        return results;
    }

    private static MultiverseStats ComputeStats(float[] results)
    {
        float sum = 0f;
        float max = float.MinValue;
        float min = float.MaxValue;

        for (int i = 0; i < results.Length; i++)
        {
            float v = results[i];
            sum += v;
            if (v > max) max = v;
            if (v < min) min = v;
        }

        Array.Sort(results);
        int half = results.Length / 2;
        float mid = results.Length % 2 == 0
            ? (results[half - 1] + results[half]) / 2f
            : results[half];

        return new MultiverseStats(sum / results.Length, max, min, mid);
    }

    // ── Multiverse（仅返回算术平均值）──

    /// <summary>
    /// Simulate <paramref name="count"/> worlds using a simple crit-rate threshold.
    /// Forks from the current curry state, binding <paramref name="varName"/> to
    /// 1f (crit) or 0f (no crit) on each iteration, and returns the average damage.
    /// </summary>
    public static float Multiverse<TDef>(
        this FluxCurryEvaluator<float, TDef> curry,
        string varName, int count, float critRate, Pcg64 rng)
        where TDef : unmanaged, IFluxExprDefinition<float>
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            bool isCrit = rng.NextFloat() < critRate;
            var fork = curry.Bind(varName, isCrit ? 1f : 0f);
            sum += fork.ForceComplete().Result;
        }
        return sum / count;
    }

    /// <summary>
    /// Simulate <paramref name="count"/> worlds using an external FluxFormula as the
    /// crit judge. The judge formula receives the generator's random float as its
    /// only variable; a result &gt; 0.5 means crit.
    /// </summary>
    public static float Multiverse<TDef, TJudgeDef>(
        this FluxCurryEvaluator<float, TDef> curry,
        string varName, int count,
        FluxAssembler<float, TJudgeDef> judge,
        FluxFormula<float, TJudgeDef> judgeFormula,
        Pcg64 rng)
        where TDef : unmanaged, IFluxExprDefinition<float>
        where TJudgeDef : unmanaged, IFluxExprDefinition<float>
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            float roll = rng.NextFloat();
            float verdict = judge.Instantiate(judgeFormula).Set("roll", roll).Run();
            bool isCrit = verdict > 0.5f;
            var fork = curry.Bind(varName, isCrit ? 1f : 0f);
            sum += fork.ForceComplete().Result;
        }
        return sum / count;
    }

    /// <summary>
    /// Simulate <paramref name="count"/> worlds using a user-defined predicate.
    /// The predicate receives the generator and returns true for crit.
    /// </summary>
    public static float Multiverse<TDef>(
        this FluxCurryEvaluator<float, TDef> curry,
        string varName, int count, Func<Pcg64, bool> predicate, Pcg64 rng)
        where TDef : unmanaged, IFluxExprDefinition<float>
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            bool isCrit = predicate(rng);
            var fork = curry.Bind(varName, isCrit ? 1f : 0f);
            sum += fork.ForceComplete().Result;
        }
        return sum / count;
    }

    // ── MultiverseStats（返回 Avg + Max + Min + Mid）──

    /// <summary>
    /// Simulate <paramref name="count"/> worlds using a simple crit-rate threshold,
    /// returning full aggregated statistics.
    /// </summary>
    public static MultiverseStats MultiverseStats<TDef>(
        this FluxCurryEvaluator<float, TDef> curry,
        string varName, int count, float critRate, Pcg64 rng)
        where TDef : unmanaged, IFluxExprDefinition<float>
    {
        var results = Simulate(curry, varName, count,
            () => rng.NextFloat() < critRate ? 1f : 0f);
        return ComputeStats(results);
    }

    /// <summary>
    /// Simulate <paramref name="count"/> worlds using an external FluxFormula as the
    /// crit judge, returning full aggregated statistics.
    /// </summary>
    public static MultiverseStats MultiverseStats<TDef, TJudgeDef>(
        this FluxCurryEvaluator<float, TDef> curry,
        string varName, int count,
        FluxAssembler<float, TJudgeDef> judge,
        FluxFormula<float, TJudgeDef> judgeFormula,
        Pcg64 rng)
        where TDef : unmanaged, IFluxExprDefinition<float>
        where TJudgeDef : unmanaged, IFluxExprDefinition<float>
    {
        var results = Simulate(curry, varName, count, () =>
        {
            float roll = rng.NextFloat();
            return judge.Instantiate(judgeFormula).Set("roll", roll).Run() > 0.5f ? 1f : 0f;
        });
        return ComputeStats(results);
    }

    /// <summary>
    /// Simulate <paramref name="count"/> worlds using a user-defined predicate,
    /// returning full aggregated statistics.
    /// </summary>
    public static MultiverseStats MultiverseStats<TDef>(
        this FluxCurryEvaluator<float, TDef> curry,
        string varName, int count, Func<Pcg64, bool> predicate, Pcg64 rng)
        where TDef : unmanaged, IFluxExprDefinition<float>
    {
        var results = Simulate(curry, varName, count,
            () => predicate(rng) ? 1f : 0f);
        return ComputeStats(results);
    }
}
