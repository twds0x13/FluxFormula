using System;
using FluxFormula.Core;

/// <summary>
/// Extension methods for multiverse (fork + simulate) damage evaluation.
/// </summary>
public static class MultiverseExtensions
{
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
            // fresh instance per iteration — judge formulas are typically tiny
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
}
